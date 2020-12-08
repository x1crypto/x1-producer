using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;
using XDS.Producer.Domain;
using XDS.Producer.Domain.Addresses;
using XDS.Producer.Domain.RPC;
using XDS.Producer.Domain.RPC.DumpWallet;
using XDS.Producer.Domain.RPC.GetBlockTemplate;
using XDS.Producer.Domain.RPC.GetUnspentOutputs;
using XDS.Producer.Domain.Tools;
using XDS.Producer.Mining;
using XDS.Producer.Staking;
using XDS.Producer.State;

namespace XDS.Producer.Services
{
    public class WorkPuller
    {
        /// <summary>
        /// If the wallet has any addresses labeled with MiningLabelPrefix,
        /// then only use those for mining, otherwise any suitable address.
        /// </summary>
        public const string MiningLabelPrefix = "Mining";

        readonly IAppConfiguration appConfiguration;
        readonly BlockTemplateCache blockTemplateCache;
        readonly StakingService stakingService;
        readonly MinerCoordinator minerCoordinator;
        readonly DeviceController deviceController;
        readonly RPCClient rpcClient;
        readonly ILogger logger;
        readonly Stopwatch stopwatch;

        public WorkPuller(IAppConfiguration appConfiguration, BlockTemplateCache blockTemplateCache, StakingService stakingService, MinerCoordinator minerCoordinator, DeviceController deviceController, RPCClient rpcClient, ILoggerFactory loggerFactory)
        {
            this.appConfiguration = appConfiguration;
            this.blockTemplateCache = blockTemplateCache;
            this.stakingService = stakingService;
            this.minerCoordinator = minerCoordinator;
            this.deviceController = deviceController;
            this.rpcClient = rpcClient;
            this.logger = loggerFactory.CreateLogger<WorkPuller>();
            this.stopwatch = new Stopwatch();
        }

        public bool HasShutDown { get; set; }

        public async Task Start()
        {
            if (this.appConfiguration.Mine)
            {
                this.deviceController.MinerAdapter = new MultiInstanceMinerAdapter<SpartacryptOpenCLMiner>(this.logger);

                try
                {
                    this.deviceController.MinerAdapter.EnsureInitialized();
                    this.deviceController.RunHashTest();
                }
                catch (Exception e)
                {
                    this.logger.LogError($"Mining is unavailable: {e.Message}");
                    return;
                }

                await EnsureMineToAddresses();
            }
            else
            {
                this.logger.LogWarning("Mining is off per configuration.");
            }

            if (this.appConfiguration.Stake)
            {
                this.logger.LogInformation("Starting Staking...");
                this.stakingService.Start();
                this.logger.LogInformation("Staking started!");
            }
            else
            {
                this.logger.LogWarning("Staking is off per configuration.");
            }

            var _ = Task.Run(PullWorkLoop);
        }



        async Task EnsureMineToAddresses()
        {
            var mineToAddresses = new List<BitcoinWitPubKeyAddress>();

            if (this.appConfiguration.MineToAddress != null)
            {
                mineToAddresses.Add(this.appConfiguration.MineToAddress);
                this.logger.LogInformation($"Mining to address {this.appConfiguration.MineToAddress} (set in xds-producer.config).");
                AddressCache.SetMineToAddresses(mineToAddresses);
                return;
            }

            this.logger.LogInformation("Trying to extract mine-to-addresses from the wallet. The wallet must contain at least one unspent output.");

            while (true)
            {
                try
                {
                    var getUnspentOutputsResponse = await this.rpcClient.GetUnspentOutputs();

                    if (getUnspentOutputsResponse.Status != 200)
                        throw new InvalidOperationException(getUnspentOutputsResponse.StatusText);

                    var unspentOutputs = getUnspentOutputsResponse.Result.result
                        .Where(x => x.address != null)
                        .DistinctBy(x => x.address).ToArray();

                    if (unspentOutputs.Length == 0)
                    {
                        throw new InvalidOperationException($"Unable to extract addresses for mining from the wallet because it doesn't contain unspent outputs. " +
                                             $"You can set an address in xds-producer config: minetoaddress=xds1...");
                    }

                    bool useMiningLabelPrefix = unspentOutputs.Any(x => HasMiningLabelPrefix(x.label));
                    this.logger.LogInformation(useMiningLabelPrefix
                            ? $"Extracting mine-to addresses where its label is prefixed with '{MiningLabelPrefix}'."
                            : $"Using all addresses as mine-to addresses, because no address has its label prefixed with '{MiningLabelPrefix}'.");

                    foreach (var output in unspentOutputs)
                    {
                        if (!output.spendable || !output.solvable)
                            continue;

                        if (useMiningLabelPrefix && !HasMiningLabelPrefix(output.label))
                            continue;

                        try
                        {
                            // use every address that is a valid WPKH address.
                            mineToAddresses.Add(new BitcoinWitPubKeyAddress(output.address, C.Network));
                        }
                        // ReSharper disable once EmptyGeneralCatchClause
                        catch
                        {
                        }
                    }

                    if (mineToAddresses.Count > 0)
                    {
                        this.logger.LogInformation($"Imported {mineToAddresses.Count} addresses for mining.");
                        AddressCache.SetMineToAddresses(mineToAddresses);
                        return;
                    }

                    throw new InvalidOperationException("No addresses for mining.");
                }
                catch (Exception e)
                {
                    this.logger.LogError($"Error retrieving addresses for mining: {e.Message} Retrying...");
                    await Task.Delay(2000);
                }
            }
        }

        bool HasMiningLabelPrefix(string label)
        {
            return label != null && label.StartsWith(MiningLabelPrefix);
        }

        async Task PullWorkLoop()
        {
            while (!this.appConfiguration.Cts.IsCancellationRequested)
            {
                try
                {
                    if (this.appConfiguration.Stake)
                    {
                        var getUnspentOutputsResponse = await this.rpcClient.GetUnspentOutputs();
                        var dumpWalletResponse = await this.rpcClient.DumpWallet(Path.Combine(this.appConfiguration.DataDirRoot.ToString(), "dumptemp.txt"));
                        if (getUnspentOutputsResponse.Status == 200 && dumpWalletResponse.Status == 200)
                            PushToCoinsCache(getUnspentOutputsResponse.Result.result, dumpWalletResponse.Result.result, false);

                    }

                    this.logger.LogInformation("Requesting block template...");
                    var getBlockTemplateResponse = await this.rpcClient.GetBlockTemplate();

                    if (getBlockTemplateResponse.Status != 200)
                    {
                        if (!this.appConfiguration.Cts.IsCancellationRequested)
                        {
                            this.logger.LogError("Block template was null, waiting 5 seconds before next request...");
                            await Task.Delay(5000);
                        }
                        continue;
                    }

                    RPCBlockTemplate blockTemplate = getBlockTemplateResponse.Result.result;

                    uint posBits = blockTemplate.ParseBits(true);
                    uint powBits = blockTemplate.ParseBits(false);

                    Target posTarget = new Target(posBits);
                    Target powTarget = new Target(powBits);

                    this.logger.LogInformation($"New block template: {blockTemplate.previousblockhash}-{blockTemplate.TemplateNumber} Block: {blockTemplate.height} Reward: {Money.Satoshis(blockTemplate.coinbasevalue)} XDS{Environment.NewLine}" +
                                               $"Proof-of-Stake:   Target: {blockTemplate.posbits}    Difficulty: {posTarget.Difficulty.ToString("0").PadLeft(7)}    Equivalent to PoW hash rate: { HashRate.EstimateGHashPerSecondFromBits(posBits).ToString("0.0").PadLeft(7)} GHash/s{Environment.NewLine}" +
                                               $"Proof-of-Work:    Target: {blockTemplate.bits}    Difficulty: {powTarget.Difficulty.ToString("0").PadLeft(7)}    Estimated network hash rate: { HashRate.EstimateGHashPerSecondFromBits(powBits).ToString("0.0").PadLeft(7)} GHash/s");


                    this.blockTemplateCache.SetBlockTemplateLocked(blockTemplate);

                    if (this.appConfiguration.Mine)
                    {
                        this.minerCoordinator.NotifyBlockTemplateReceived();
                    }

                }
                catch (Exception e)
                {
                    this.stakingService.BlockTemplate = null;
                    if (!this.appConfiguration.Cts.IsCancellationRequested)
                        Console.WriteLine($"Error in PullWorkLoop: {e.Message}");
                }

                await Task.Delay(1000);
            }

            HasShutDown = true;
        }

        void PushToCoinsCache(RPCUnspentOutput[] unspentOutputs, RPCFilename dumpFilePath, bool export = false)
        {
            this.stopwatch.Restart();

            try
            {
                var dump = File.ReadAllText(dumpFilePath.filename);
                File.Delete(dumpFilePath.filename);
                var lookup = ParseDump(dump);

                var coins = new List<SegWitCoin>();
                foreach (var utxo in unspentOutputs)
                {
                    if (utxo.confirmations < (export ? 0 : 125))
                        continue;

                    if (lookup.ContainsKey(utxo.address))
                    {
                        var coin = new SegWitCoin(lookup[utxo.address], uint256.Parse(utxo.txid), utxo.vout, (long)(utxo.amount * C.SatoshisPerCoin), UtxoType.NotSet);
                        coins.Add(coin);
                    }
                    else
                    {
                        this.logger.LogWarning($"For utxo {utxo.txid}-{utxo.vout}, its address {utxo.address} was not found in the lookup.");
                    }
                }

                if (export)
                    Export(coins);

                CoinCache.ReplaceCoins(coins);

                this.logger.LogInformation($"Cached {coins.Count} mature coins for staking - {stopwatch.ElapsedMilliseconds} ms.");
            }
            catch (Exception e)
            {
                this.logger.LogError($"Error processing  wallet dump file {e.Message}");
                throw;
            }
        }

        private void Export(List<SegWitCoin> coins)
        {
            var sb = new StringBuilder();
            foreach (var coin in coins)
            {
                byte[] bytes = ((PubKeyHashAddress)coin.SegWitAddress).KeyMaterial.PlaintextBytes;
                sb.AppendLine(
                    $"bitcoin-cli importprivkey {Encoders.Hex.EncodeData(bytes)} utxo-bech32 false");
            }

            var fileText = sb.ToString();
            File.WriteAllText(Path.Combine(this.appConfiguration.DataDirRoot.ToString(), "reimport-utxos.bat"), fileText);
        }



        Dictionary<string, PubKeyHashAddress> ParseDump(string dump)
        {
            if (dump == null)
                throw new ArgumentNullException(nameof(dump));

            string[] lines = dump.Split(
                new[] { "\r\n", "\r", "\n" },
                StringSplitOptions.RemoveEmptyEntries
            );

            var lookup = new Dictionary<string, PubKeyHashAddress>();

            foreach (var line in lines)
            {
                if (line.StartsWith("#"))
                    continue;

                if (line.StartsWith("00")) // script=1
                    continue;

                string[] lineItems = line.Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries);

                if (lineItems.Length < 2)
                    continue;

                BitcoinSecret bitcoinSecret;
                string wif = lineItems[0];
                try
                {
                    bitcoinSecret = new BitcoinSecret(wif, C.Network); // xds main has same SECRET_KEY prefix as BTC main
                    if (!bitcoinSecret.PubKey.IsCompressed)
                    {
                        throw new InvalidOperationException("Expected compressed pubkey!");
                    }
                }
                catch (Exception e)
                {
                    this.logger.LogWarning($"Invalid WIF '{wif}': {e.Message}, skipping.");
                    continue;
                }

                string address = null;
                for (var i = 1; i < lineItems.Length; i++)
                {
                    var item = lineItems[i];
                    if (item.StartsWith("addr="))
                    {
                        // addr=bech32 - one address
                        if (item.StartsWith("addr=xds1"))
                        {
                            address = item.Replace("addr=", "").Trim();
                            continue;
                        }

                        // addr=1...,3...,xds1... - also legacy formats
                        string[] severalAdr = item.Split(new[] { "," }, StringSplitOptions.None);
                        for (var j = 0; j < severalAdr.Length; j++)
                        {
                            if (severalAdr[j].StartsWith("xds1"))
                                address = severalAdr[j].Trim();
                        }
                    }

                }

                if (!string.IsNullOrWhiteSpace(address))
                {
                    if (address.Length == C.PubKeyHashAddressLength)
                    {
                        var scriptPubKey = bitcoinSecret.PrivateKey.PubKey.Compress().WitHash.ScriptPubKey;

                        var pubKeyHashAddress = new PubKeyHashAddress
                        {
                            KeyMaterial = new KeyMaterial
                            {
                                PlaintextBytes = bitcoinSecret.PrivateKey.ToBytes().Take(32).ToArray(),
                                KeyType = KeyType.Imported
                            },
                            AddressType = AddressType.PubKeyHash,
                            ScriptPubKeyHex = scriptPubKey.ToHex(),
                            Address = scriptPubKey.GetAddressFromScriptPubKey()
                        };

                        Debug.Assert(scriptPubKey == new Key(pubKeyHashAddress.KeyMaterial.PlaintextBytes).PubKey.WitHash.ScriptPubKey);
                        if (address == pubKeyHashAddress.Address)
                        {
                            if (!lookup.ContainsKey(address))
                                lookup.Add(address, pubKeyHashAddress);
                            else
                                this.logger.LogWarning($"Lookup already contains address {address}.");
                        }
                        else
                            this.logger.LogWarning($"Address {address} cannot be created from WIF {wif}, skipping.");
                    }
                    else if (address.Length == C.ScriptAddressLength)
                        this.logger.LogWarning($"Address {address} looks like a script address, skipping.");
                    else
                        this.logger.LogWarning($"Address '{address}' has an invalid length!");
                }
            }

            return lookup;
        }
    }
}
