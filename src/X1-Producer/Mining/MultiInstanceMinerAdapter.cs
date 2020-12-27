using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using Cloo;
using Microsoft.Extensions.Logging;

namespace X1.Producer.Mining
{
    public class DeviceDescription
    {
        public ComputeDevice ComputeDevice;

        public long GlobalWorkSizeProduct;
        internal long MaxWorkGroupSize;
        internal long MaxComputeUnits;
        internal long MaxClockMhz;
        public string Name;
        public long[] WorkItemSizes;
        internal string OpenCLVersion;
    }

    public sealed class MultiInstanceMinerAdapter<T> : IMinerAdapter where T : IMiningDevice, new()
    {
        readonly object lockObject = new object();
        readonly ILogger logger;

        ConcurrentDictionary<int, DeviceDescription> deviceDescriptions;

        public MultiInstanceMinerAdapter(ILogger logger)
        {
            this.logger = logger;
        }

        public int InstancesCount => this.deviceDescriptions.Count;

        public DeviceDescription GetDeviceDescription(int index)
        {
            return this.deviceDescriptions[index];
        }

        public string GetDeviceName(int index)
        {
            return this.deviceDescriptions[index].Name;
        }

        public uint Mine(byte[] headerBytes, byte[] targetBytes, uint startNonce, uint maxNonce, int instanceIndex, out long elapsedMilliseconds)
        {
            var iterations = maxNonce - startNonce;

            using var instance = GetMinerInstance(instanceIndex);

            var result = instance.FindProofOfWork(headerBytes, targetBytes, startNonce, iterations, out elapsedMilliseconds);

            if (result == 0 || result == uint.MaxValue)
                return uint.MaxValue;

            VerifyProofOfWork(result, headerBytes, targetBytes);

            return result;
        }

        void VerifyProofOfWork(uint result, byte[] headerBytes, byte[] targetBytes)
        {
            var nonceBytes = BitConverter.GetBytes(result);
            Buffer.BlockCopy(nonceBytes, 0, headerBytes, 76, 4);
            var hash = NBitcoin.Altcoins.X1Crypto.Sha512T.GetHash(headerBytes, 0, headerBytes.Length);
            var comp = IsTargetGreaterThanHash(targetBytes, hash);

            if (comp < 0)
            {
                throw new InvalidOperationException("The miner instance returned an invalid nonce. This is likely a bug in the miner code.");
            }
        }

        public void EnsureInitialized()
        {
            if (this.deviceDescriptions == null)
            {
                CreateDeviceDescriptions();

                if (this.deviceDescriptions.Count == 0)
                {
                    throw new InvalidOperationException("No usable devices!");
                }
            }

        }

        T GetMinerInstance(int instanceIndex)
        {
            var miner = new T();
            miner.AttachToDevice(this.deviceDescriptions[instanceIndex].ComputeDevice);
            return miner;
        }

        void CreateDeviceDescriptions()
        {
            lock (lockObject)
            {
                if (this.deviceDescriptions != null)
                    return;

                var dict = new ConcurrentDictionary<int, DeviceDescription>();

                var computeDevices = ComputePlatform.Platforms.SelectMany(p => p.Devices).Where(d => d.Available && d.CompilerAvailable && !d.Name.Contains("Intel")).ToList();
                if (!computeDevices.Any())
                {
                    computeDevices = ComputePlatform.Platforms.SelectMany(p => p.Devices).Where(d => d.Available && d.CompilerAvailable && d.Name.Contains("Intel")).ToList();
                    if (!computeDevices.Any())
                    {
                        this.logger.LogError("No OpenCL Devices Found!");
                        return;
                    }
                }

                var sb = new StringBuilder();

                for (var i = 0; i < computeDevices.Count; i++)
                {
                    ComputeDevice device = computeDevices[i];

                    sb.AppendLine($"OpenCL device {i}: Name: {device.Name}, MaxWorkGroupSize: {device.MaxWorkGroupSize} MaxComputeUnits: {device.MaxComputeUnits}.");

                    sb.Append("MaxWorkItemSizes: ");

                    long workMax = 1;
                    var workItemSizes = device.MaxWorkItemSizes.ToArray();

                    foreach (long size in workItemSizes)
                    {
                        sb.Append(size + " ");
                        workMax *= size;
                    }

                    sb.AppendLine();

                    var description = new DeviceDescription
                    {
                        ComputeDevice = device,
                        Name = device.Name,
                        GlobalWorkSizeProduct = workMax,
                        WorkItemSizes = device.MaxWorkItemSizes.ToArray(),
                        MaxWorkGroupSize = device.MaxWorkGroupSize,
                        MaxComputeUnits = device.MaxComputeUnits,
                        MaxClockMhz = device.MaxClockFrequency,
                        OpenCLVersion = device.OpenCLCVersionString
                    }; 
                    
                    dict[i] = description;
                }

                sb.Append($"Discovered {dict.Count} device(s).");

                this.logger.LogInformation(sb.ToString());

                this.deviceDescriptions = dict;
            }
        }

        /// <summary>
        /// If a is > b, return 1.
        /// The condition is met if the Target is greater or equal the Hash and the method returns 1 or 0.
        /// </summary>
        /// <param name="a">Target</param>
        /// <param name="b">Hash</param>
        /// <returns>1, -1 or 0</returns>
        int IsTargetGreaterThanHash(byte[] a, byte[] b)
        {
            for (int i = a.Length; i-- > 0;)
            {
                if (a[i] > b[i])
                    return 1;
                if (a[i] < b[i])
                    return -1;
            }
            return 0;
        }
    }
}
