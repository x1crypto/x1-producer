using System;
using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using NBitcoin;
using X1.Producer.Domain;

namespace X1.Producer.Mining
{
    public class DeviceController
    {
        readonly ILogger logger;

        public readonly ConcurrentDictionary<int, PerfCounter> PerfCounters = new ConcurrentDictionary<int, PerfCounter>();

        public IMinerAdapter MinerAdapter;

        public DeviceController(ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger<DeviceController>();
        }

        public uint Start(byte[] headerBytes, byte[] targetBytes, uint startNonce, uint maxNonce, int threadIndex)
        {
            uint nonce = MinerAdapter.Mine(headerBytes, targetBytes, startNonce, maxNonce, threadIndex, out var elapsedMilliseconds);

            PerfCounters[threadIndex].LastHashRate = HashRate.GetMHashPerSecond(startNonce, maxNonce, elapsedMilliseconds);

            return nonce;
        }

        public void RunHashTest()
        {
            this.logger.LogInformation("Running self test...");

            var sb = new StringBuilder();

            for (var i = 0; i < MinerAdapter.InstancesCount; i++)
            {
                if (!PerfCounters.ContainsKey(i))
                    PerfCounters[i] = new PerfCounter();

                var block = TestBlockHeaderData.CreateTestBlock();

                var currentWork = new SlimBlockHeader
                {
                    Bits = TestBlockHeaderData.GenesisBits,
                    HashPrevBlock = uint256.Zero.ToBytes(),
                    MerkleRoot = block.GetMerkleRoot().Hash.ToBytes(),
                    Nonce = 0,
                    Timestamp = (uint)block.Header.BlockTime.ToUnixTimeSeconds(),
                    Version = block.Header.Version,
                    Data = null,
                };

                currentWork.Data = currentWork.SerializeTo80Bytes();

                uint resultNonce = Start(currentWork.Data, TestBlockHeaderData.GenesisMiningBits.ToUInt256().ToBytes(), startNonce:  0, maxNonce: TestBlockHeaderData.GenesisNonce + 1, i);

                if (resultNonce != TestBlockHeaderData.GenesisNonce)
                {
                    sb.AppendLine($"Self test failed: Expected nonce was {TestBlockHeaderData.GenesisNonce}, but the nonce returned was {resultNonce}.");
                    this.logger.LogCritical(sb.ToString());
                    Environment.Exit(1);
                }

                currentWork.Timestamp++;
                currentWork.Data = currentWork.SerializeTo80Bytes();

                uint nonce2 = Start(currentWork.Data, TestBlockHeaderData.GenesisMiningBits.ToUInt256().ToBytes(), startNonce:  0, maxNonce: TestBlockHeaderData.GenesisNonce + 1, 0);
                if (nonce2 != 12803398)
                {
                    sb.AppendLine($"Self test failed: Expected nonce was {12803398}, but the nonce returned was {nonce2}.");
                    this.logger.LogCritical(sb.ToString());
                    Environment.Exit(1);
                }

                sb.Append($"Device {i} is working, {PerfCounters[i].LastHashRate} MHash/s");
            }

            this.logger.LogInformation(sb.ToString());
        }
    }
}
