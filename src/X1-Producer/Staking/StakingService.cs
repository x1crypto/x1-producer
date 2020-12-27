using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.BouncyCastle.Math;
using NBitcoin.Crypto;
using NBitcoin.DataEncoders;
using X1.Producer.Domain;
using X1.Producer.Domain.Addresses;
using X1.Producer.Domain.RPC;
using X1.Producer.Domain.RPC.GetBlockTemplate;
using X1.Producer.Domain.Tools;
using X1.Producer.Services;
using X1.Producer.State;

namespace X1.Producer.Staking
{
    public sealed class StakingService
    {
        readonly ILogger logger;
        public readonly StakingStatus Status;
        public readonly PosV3 PosV3;
        readonly Task stakingTask;
        readonly IAppConfiguration appConfiguration;
        readonly RPCClient rpcClient;
        readonly BlockTemplateCache blockTemplateCache;

        readonly Stopwatch stopwatch;

        bool isStaking;

        public RPCBlockTemplate BlockTemplate { get; set; }

        public StakingService(ILoggerFactory loggerFactory, IAppConfiguration appConfiguration, BlockTemplateCache blockTemplateCache, RPCClient rpcClient)
        {
            this.logger = loggerFactory.CreateLogger<StakingService>();
            this.appConfiguration = appConfiguration;
            this.blockTemplateCache = blockTemplateCache;
            this.rpcClient = rpcClient;
            this.stakingTask = new Task(StakingLoop, appConfiguration.Cts.Token);
            this.stopwatch = Stopwatch.StartNew();
            this.Status = new StakingStatus { StartedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            this.PosV3 = new PosV3 { SearchInterval = 64, BlockInterval = 4 * 64 };
        }

        public void Start()
        {
            this.isStaking = true;
            if (this.stakingTask.Status != TaskStatus.Running)
            {
                this.stakingTask.Start();
            }
        }

        public void Stop()
        {
            this.isStaking = false;
        }

        void StakingLoop()
        {
            long previousMaskedTime = CreateMaskedTimeFromClockTime();
            uint256 previousStakemodifierV2 = uint256.Zero;

            while (!this.appConfiguration.Cts.Token.IsCancellationRequested)
            {
                while (isStaking)
                {
                    try
                    {
                        Task.Delay(1000).Wait();

                        if (!EnsureBlockTemplate())
                        {
                            //this.logger.LogWarning("No block template, waiting...");
                            continue;
                        }

                        this.PosV3.CurrentBlockTime = CreateMaskedTimeFromClockTime();

                        this.PosV3.StakeModifierV2 = this.BlockTemplate.ParseStakeModifierV2();

                        bool timeChanged = this.PosV3.CurrentBlockTime > previousMaskedTime;

                        if (timeChanged && HeaderTimeChecksPosRule((uint)this.PosV3.CurrentBlockTime))
                        {
                            previousMaskedTime = this.PosV3.CurrentBlockTime;

                            this.stopwatch.Restart();
                            (int outputs, int found) = Stake();
                            this.Status.ComputeTimeMs = this.stopwatch.ElapsedMilliseconds;
                            this.logger.LogInformation($"Staked at height {this.BlockTemplate.height}, found {found} kernels in {outputs} utxos - {this.Status.ComputeTimeMs} ms.{Environment.NewLine}" +
                                                       $"{this.Status.BlocksAccepted} Proof-of-Stake blocks submitted, {this.Status.Exceptions} errors, last error: {this.Status.LastException}");
                        }
                    }
                    catch (Exception e)
                    {
                        HandleError(e);
                    }
                }

                Task.Delay(1000);
            }

            void HandleError(Exception e)
            {
                this.Status.LastException = e.Message.Replace(":", "-");
                this.Status.Exceptions++;
                this.logger.LogError(e.ToString());
            }

        }

        /// <summary>Checks if the timstamp of a proof-of-stake block is greater than previous block timestamp. Equal or lower is a consensus error.</summary>
        bool HeaderTimeChecksPosRule(uint timeToCheck)
        {
            if (timeToCheck <= this.BlockTemplate.GetPreviousBlockTime())
                return false;
            return true;
        }

        bool EnsureBlockTemplate()
        {
            this.BlockTemplate = this.blockTemplateCache.GetClonedBlockTemplateLocked();
            return this.BlockTemplate != null && this.BlockTemplate.height % 2 == 0;
        }

        (int outputs, int found) Stake()
        {
            uint bits = this.BlockTemplate.ParseBits(true);
            Target targetBits = new Target(bits);

            this.PosV3.Target = targetBits.ToUInt256(); // same for display only
            this.PosV3.TargetDifficulty = targetBits.Difficulty;
            this.PosV3.TargetAsBigInteger = bits.ToBouncyCastleBigInteger(); // for calculation


            this.Status.NetworkWeight = GetNetworkWeight();
            this.Status.ExpectedTime = GetExpectedTime(this.Status.NetworkWeight, out this.Status.WeightPercent);

            var coins = CoinCache.GetCoinsLocked();
            this.Status.UnspentOutputs = coins.Length;
            this.Status.Weight = coins.Sum(x => x.UtxoValue);

            var validKernels = FindValidKernels(coins);

            this.Status.KernelsFound = validKernels.Count;


            if (validKernels.Count > 0)
                CreateNextBlock(validKernels);

            return (coins.Length, validKernels.Count);
        }



        List<SegWitCoin> FindValidKernels(SegWitCoin[] coins)
        {
            var validKernels = new List<SegWitCoin>();
            foreach (var c in coins)
            {
                if (CheckStakeKernelHash(c))
                {
                    validKernels.Add(c);
                    break;
                }

            }
            return validKernels;
        }

        bool CheckStakeKernelHash(SegWitCoin segWitCoin)
        {
            BigInteger value = BigInteger.ValueOf(segWitCoin.UtxoValue);
            BigInteger weightedTarget = this.PosV3.TargetAsBigInteger.Multiply(value);

            uint256 kernelHash;
            using (var ms = new MemoryStream())
            {
                var serializer = new BitcoinStream(ms, true);
                serializer.ReadWrite(this.PosV3.StakeModifierV2);

                var utxoTxHash = segWitCoin.UtxoTxHash;

                serializer.ReadWrite(utxoTxHash);
                serializer.ReadWrite(segWitCoin.UtxoTxN);
                serializer.ReadWrite((uint)this.PosV3.CurrentBlockTime); // be sure this is uint
                kernelHash = Hashes.DoubleSHA256(ms.ToArray());
            }

            var hash = new BigInteger(1, kernelHash.ToBytes(false));

            return hash.CompareTo(weightedTarget) <= 0;
        }




        void CreateNextBlock(List<SegWitCoin> kernelCoins)
        {
            SegWitCoin kernelCoin = kernelCoins[0];

            // use smallest amount
            foreach (var coin in kernelCoins)
                if (coin.UtxoValue < kernelCoin.UtxoValue)
                    kernelCoin = coin;

            var newBlockHeight = this.BlockTemplate.height;

            long totalReward = this.BlockTemplate.coinbasevalue;

            Transaction tx = CoinstakeTransactionService.CreateAndSignCoinstakeTransaction(kernelCoin, totalReward, (uint)this.PosV3.CurrentBlockTime, null, out Key blockSignatureKey);
            this.logger.LogInformation($"Coinstake tx: {tx.GetHash()}");

            SlimBlock slimBlock = this.BlockTemplate.CreateTemplateBlock((uint)this.PosV3.CurrentBlockTime, true, null, tx, blockSignatureKey);

            if (TestTime.HasValue)
            {
                TestSlimBlock = slimBlock;
                isStaking = false;
                TestTime = null;
                return;
            }

            this.logger.LogWarning($"Submitting block {slimBlock.Height}-{slimBlock.SlimBlockHeader.GetDoubleSHA256()}, previous block {new uint256(slimBlock.SlimBlockHeader.HashPrevBlock)}, {kernelCoin.SegWitAddress.Address}, {Convert.ToDecimal(kernelCoin.UtxoValue) / C.SatoshisPerCoin}.");

            try
            {
                var hexBlock = Encoders.Hex.EncodeData(slimBlock.Serialize());
                var response = this.rpcClient.SubmitBlock(hexBlock).ConfigureAwait(false).GetAwaiter().GetResult();
                if (response.Status != 200)
                    throw new InvalidOperationException($"{response.StatusText}");
            }
            catch (Exception)
            {
                this.Status.BlocksNotAccepted++;
                throw;
            }

            this.Status.BlocksAccepted += 1;

            this.logger.LogWarning(
                $"Congratulations, your staked a new block at height {newBlockHeight} and received a total reward of {(decimal)totalReward / C.SatoshisPerCoin} X1.");
        }

        double GetNetworkWeight()
        {                                               // ‭4294967296‬
            var result = this.PosV3.TargetDifficulty * 0x100000000; // https://bitcoin.stackexchange.com/questions/76870/in-pos-how-is-net-stake-weight-calculated
            if (result > 0)                                         // idea: amount of work / amount of time
            {
                result /= this.PosV3.BlockInterval;
                result *= this.PosV3.SearchInterval;
                return result;
            }
            return 0;
        }

        int GetExpectedTime(double networkWeight, out double ownPercent)
        {
            if (this.Status.Weight <= 0)
            {
                ownPercent = 0;
                return int.MaxValue;
            }

            var ownWeight = (double)this.Status.Weight;

            var ownFraction = ownWeight / networkWeight;

            var expectedTimeSeconds = this.PosV3.BlockInterval / ownFraction;
            ownPercent = Math.Round(ownFraction * 100, 1);

            return (int)(this.PosV3.BlockInterval + expectedTimeSeconds);
        }



        public long? TestTime { get; set; }

        public SlimBlock TestSlimBlock { get; set; }

        long CreateMaskedTimeFromClockTime()
        {
            if (TestTime.HasValue)
                return TestTime.Value;

            // todo: use adjusted time or is this good enough?
            long currentAdjustedTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long blockTime = currentAdjustedTime - currentAdjustedTime % this.PosV3.SearchInterval;

            return blockTime;
        }
    }
}
