using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;
using X1.Producer.Domain;
using X1.Producer.Domain.RPC;
using X1.Producer.Domain.RPC.GetBlockTemplate;
using X1.Producer.Services;
using X1.Producer.State;

namespace X1.Producer.Mining
{
    public class MinerCoordinator
    {
        readonly ILogger logger;
        readonly IAppConfiguration appConfiguration;
        readonly RPCClient rpcClient;
        readonly BlockTemplateCache blockTemplateCache;
        readonly DeviceController deviceController;
        readonly Stopwatch stopwatchCancellation = new Stopwatch();
        readonly List<Task> minerTasks = new List<Task>();

        bool isCancelled;

        public MinerCoordinator(ILoggerFactory loggerFactory, IAppConfiguration appConfiguration, BlockTemplateCache blockTemplateCache, DeviceController deviceController, RPCClient rpcClient)
        {
            this.logger = loggerFactory.CreateLogger<MinerCoordinator>();
            this.appConfiguration = appConfiguration;
            this.rpcClient = rpcClient;
            this.blockTemplateCache = blockTemplateCache;
            this.deviceController = deviceController;
        }

        public void CancelWaitResume()
        {
            stopwatchCancellation.Restart();

            // cancel
            isCancelled = true;

            // wait
            if (minerTasks.Count > 0)
            {
                // print last stats
                var sb = new StringBuilder();
                sb.AppendLine("Mining Stats:");
                for (var i = 0; i < this.deviceController.PerfCounters.Count; i++)
                {
                    sb.AppendLine($"Device {i}: Type: {this.deviceController.PerfCounters[i].DeviceName}, Average hash rate: {this.deviceController.PerfCounters[i].HashRates.Sum(x => x) / this.deviceController.PerfCounters[i].HashRates.Count} MHash/s {this.deviceController.PerfCounters[i].HashRates.Count} samples.");
                    sb.AppendLine(this.deviceController.PerfCounters[i].Message);
                }

                // wait till the tasks have completed
                Task.WaitAll(minerTasks.ToArray());
                sb.Append($"{minerTasks.Count} miner thread(s) ran to completion - {stopwatchCancellation.ElapsedMilliseconds} ms.");

                this.logger.LogInformation(sb.ToString());
                minerTasks.Clear();
            }

            // resume
            isCancelled = false;
        }

        public void NotifyBlockTemplateReceived(bool isPowAllowed)
        {
            // this should not block the WorkPuller, and CancelWaitResume can take a while to return
            Task.Run(() =>
            {
                CancelWaitResume();

                if(!isPowAllowed)
                    return;
                
                var minerContexts = CreateMinerContexts(this.deviceController.MinerAdapter.InstancesCount);

                foreach (var context in minerContexts)
                {
                    var minerTask = Task.Factory.StartNew(WorkerThread, context, TaskCreationOptions.LongRunning);
                    minerTasks.Add(minerTask);
                }
            });
        }

        List<MinerContext> CreateMinerContexts(int minerCount)
        {

            BitcoinWitPubKeyAddress[] mineToAddresses = AddressCache.GetMineToAddresses(minerCount);

            var contexts = new List<MinerContext>(minerCount);

            for (int i = 0; i < minerCount; i++)
            {
                // if the device index is disabled by configuration, don't create MinerContext
                if(this.appConfiguration.DisabledDeviceIndices.Contains(i))
                    continue;

                // this gets us the latest block template, including a unique extra nonce
                var blockTemplate = this.blockTemplateCache.GetClonedBlockTemplateLocked();

                // calculate the start value for block time
                var earliestTimeVsTime = blockTemplate.curtime + 1; // curtime takes into account adjusted time and prev block median time past
                var earliestTimeVsPrevTime = blockTemplate.previousblocktime + 1; // x1 consensus requires the block time > last block time
                var newPoWBlockMinTime = Math.Max(earliestTimeVsTime, earliestTimeVsPrevTime); // time is guaranteed to be > time(prev block) and most likely curtime +1

                // the header of this block is complete, with the lowest valid time, and a nonce of 0
                var block = blockTemplate.CreateTemplateBlock(newPoWBlockMinTime, false, mineToAddresses[i].ScriptPubKey, null, null);

                var minerContext = new MinerContext
                {
                    Name = $"{this.appConfiguration.ClientId} #{i}",
                    ThreadStartedUtc = DateTime.UtcNow,
                    StartNonce = 0,
                    MaxNonce = uint.MaxValue - 1,
                    ThreadIndex = i,
                    SlimBlock = block
                };

                contexts.Add(minerContext);

                if (!this.deviceController.PerfCounters.ContainsKey(i))
                    this.deviceController.PerfCounters.TryAdd(i, new PerfCounter());
            }

            return contexts;
        }

        void WorkerThread(object state)
        {
            var samples = new Dictionary<double, long>();

            var minerContext = (MinerContext)state;
            var perfCounter = this.deviceController.PerfCounters[minerContext.ThreadIndex];
            perfCounter.HashRates.Clear();
            perfCounter.LastHashRate = 0;

            long deviceWorkMax = this.deviceController.MinerAdapter.GetDeviceDescription(minerContext.ThreadIndex).GlobalWorkSizeProduct;
            var deviceMinWork = deviceWorkMax / 3;
            deviceMinWork = deviceMinWork - (deviceMinWork % 64);

            if (string.IsNullOrWhiteSpace(perfCounter.DeviceName))
                perfCounter.DeviceName = this.deviceController.MinerAdapter.GetDeviceName(minerContext.ThreadIndex);

            if (!perfCounter.IsTuned)
            {
                this.logger.LogInformation($"Tuning device { perfCounter.DeviceName}, starting at {deviceMinWork}.");
                perfCounter.DeviceBatchSize = deviceMinWork;
            }

            try
            {
                this.logger.LogInformation($"Selecting batch size {perfCounter.DeviceBatchSize} for device {perfCounter.DeviceName}, tuned={perfCounter.IsTuned}.");

                byte[] targetBytes = new Target(minerContext.SlimBlock.SlimBlockHeader.Bits).ToUInt256().ToBytes();
                byte[] headerBytes = minerContext.SlimBlock.SlimBlockHeader.SerializeTo80Bytes();

                long workDone = 0;
                long currentStart = minerContext.StartNonce;
                long currentMax = minerContext.StartNonce + perfCounter.DeviceBatchSize; // for safety, do all operations on the nonce as long, not uint
                Debug.Assert(currentMax < uint.MaxValue);

                uint extraTime = 0;
                int wasWorse = 0;

            mine:
                uint resultNonce = this.deviceController.Start(headerBytes, targetBytes, (uint)currentStart, (uint)currentMax, minerContext.ThreadIndex);

                double percent = (double)workDone / (minerContext.MaxNonce - minerContext.StartNonce) * 100.0;
                perfCounter.Message =
                    $"{minerContext.Name}, device {minerContext.ThreadIndex} tried {percent:0.00} % of nonce range, extra time {extraTime} seconds, {perfCounter.BlocksFound} blocks found," +
                    $" {perfCounter.Errors} errors, last error: {perfCounter.LastError}{Environment.NewLine}" +
                    $"Hash rate {perfCounter.LastHashRate:0.00} MHash/s, last work size {perfCounter.DeviceBatchSize}.";
                perfCounter.HashRates.Enqueue(perfCounter.LastHashRate);
                if (perfCounter.HashRates.Count > 100)
                    perfCounter.HashRates.TryDequeue(out var _);




                if (resultNonce != uint.MaxValue)
                {
                    // The matching nonce was found. In this case, we can just update the header and serialize all.
                    minerContext.SlimBlock.SlimBlockHeader.Nonce = resultNonce;
                    minerContext.SlimBlock.SlimBlockHeader.Data = headerBytes; // cover the increased timestamp
                    minerContext.SlimBlock.SlimBlockHeader.SetFinalNonce(resultNonce); // update data with nonce

                    var hexBlock = Encoders.Hex.EncodeData(minerContext.SlimBlock.Serialize());

                    var result = this.rpcClient.SubmitBlock(hexBlock).Result;
                    if (result.Status == 200)
                    {
                        perfCounter.BlocksFound++;
                        this.logger.LogWarning($"Submitted mined block {minerContext.SlimBlock.Height}-{minerContext.SlimBlock.SlimBlockHeader.GetDoubleSHA256()}: {result.Status} - {result.StatusText} - stopping all threads.");
                    }
                    else
                    {
                        perfCounter.Errors++;
                        perfCounter.LastError = result.StatusText;
                        this.logger.LogError($"Error submitting mined block: {result.Status} - {result.StatusText} - stopping all threads.");
                    }

                    this.isCancelled = true;
                    return;
                }

                workDone += perfCounter.DeviceBatchSize;


                if (isCancelled)
                {
                    return;
                }

                if (!perfCounter.IsTuned)
                {
                    if (!samples.ContainsKey(perfCounter.LastHashRate))
                        samples.Add(perfCounter.LastHashRate, perfCounter.DeviceBatchSize);

                    var averageOfBestThree = samples.OrderByDescending(x => x.Key).Take(3).Sum(x => x.Key) / 3;
                    if (perfCounter.LastHashRate <= averageOfBestThree)
                    {
                        wasWorse++;
                        if (wasWorse > 50)
                        {
                            var best = samples.OrderByDescending(x => x.Key).First();
                            perfCounter.DeviceBatchSize = best.Value;
                            this.logger.LogInformation($"Tuning finished! Selecting {perfCounter.DeviceBatchSize} as work size for { perfCounter.DeviceName} targeting {best.Key} MHash/s");
                            perfCounter.IsTuned = true;
                        }
                    }
                    else
                    {
                        this.logger.LogInformation($"{perfCounter.DeviceName}: Tuning: {perfCounter.LastHashRate} MHash/s (Batch Size: {perfCounter.DeviceBatchSize})");
                    }

                    perfCounter.DeviceBatchSize += 16384 * 8;
                }


                if (currentMax + perfCounter.DeviceBatchSize < minerContext.MaxNonce) // if we stay below MaxNonce when adding the next batch, just do it
                {
                    currentStart += perfCounter.DeviceBatchSize;
                    currentMax += perfCounter.DeviceBatchSize;

                    goto mine;
                }

                // we ran out of nonce space, increase time stamp and reset nonce space
                minerContext.SlimBlock.SlimBlockHeader.Timestamp++;
                extraTime++;
                headerBytes = minerContext.SlimBlock.SlimBlockHeader.SerializeTo80Bytes();
                currentStart = minerContext.StartNonce;
                currentMax = minerContext.StartNonce + perfCounter.DeviceBatchSize;
                workDone = 0;
                goto mine;

            }
            catch (Exception e)
            {
                var error = $"Miner Exception, Device{minerContext.ThreadIndex}: {e.Message}";
                this.logger.LogError(error);
                perfCounter.LastError = error;
                isCancelled = true;
            }
        }
    }
}
