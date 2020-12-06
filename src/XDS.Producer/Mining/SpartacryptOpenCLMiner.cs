using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Cloo;

namespace XDS.Producer.Mining
{
    public sealed class SpartacryptOpenCLMiner : IMiningDevice
    {
#if DEBUG
        readonly string openCLSourcePath = System.IO.Path.Combine("Mining", "OpenCL", nameof(SpartacryptOpenCLMiner));
        readonly string[] openCLSourceFiles = { "opencl_device_info.h", "opencl_misc.h", "opencl_sha2_common.h", "opencl_sha512.h", "sha512_miner.cl" };
#endif
        readonly string openCLKernelFunction = "kernel_find_pow";
        readonly Stopwatch stopwatch = new Stopwatch();

        List<ComputeKernel> computeKernels = new List<ComputeKernel>();
        ComputeProgram computeProgram;
        ComputeContext computeContext;
        ComputeKernel computeKernel;
        ComputeDevice computeDevice;
        string[] openCLSources;
        bool isDisposed;

        public uint FindProofOfWork(byte[] header, byte[] bits, uint nonceStart, uint iterations, out long elapsedMilliseconds)
        {
            this.stopwatch.Restart();

            using var headerBuffer = new ComputeBuffer<byte>(this.computeContext, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, header);
            using var bitsBuffer = new ComputeBuffer<byte>(this.computeContext, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, bits);
            using var powBuffer = new ComputeBuffer<uint>(this.computeContext, ComputeMemoryFlags.WriteOnly, 1);

            this.computeKernel.SetMemoryArgument(0, headerBuffer);
            this.computeKernel.SetMemoryArgument(1, bitsBuffer);
            this.computeKernel.SetValueArgument(2, nonceStart);
            this.computeKernel.SetMemoryArgument(3, powBuffer);

            using var commands = new ComputeCommandQueue(this.computeContext, this.computeDevice, ComputeCommandQueueFlags.None);
            commands.Execute(this.computeKernel, null, new long[] { iterations }, null, null);

            var nonceOut = new uint[1];
            commands.ReadFromBuffer(powBuffer, ref nonceOut, true, null);
            commands.Finish();

            elapsedMilliseconds = this.stopwatch.ElapsedMilliseconds;

            return nonceOut[0];
        }

        public void AttachToDevice(object device)
        {
            this.computeDevice = device as ComputeDevice ?? throw new ArgumentException($"{nameof(ComputeDevice)} required", nameof(device));

            InitComputeKernel();
        }

        void GetOpenCLSources()
        {

#if DEBUG
            this.openCLSources = new string[openCLSourceFiles.Length];

            for (var i = 0; i < openCLSourceFiles.Length; i++)
            {
                openCLSources[i] = System.IO.File.ReadAllText(System.IO.Path.Combine(this.openCLSourcePath, this.openCLSourceFiles[i]));
            }
#else
            this.openCLSources = new[]
            {
                Properties.Resources.SpartacryptOpenCLMiner_opencl_device_info_h,
                Properties.Resources.SpartacryptOpenCLMiner_opencl_misc_h,
                Properties.Resources.SpartacryptOpenCLMiner_opencl_sha2_common_h,
                Properties.Resources.SpartacryptOpenCLMiner_opencl_sha512_h,
                Properties.Resources.SpartacryptOpenCLMiner_sha512_miner_cl
            };
#endif
        }

        void InitComputeKernel()
        {
            if (this.openCLSources == null)
                GetOpenCLSources();

            var properties = new ComputeContextPropertyList(this.computeDevice.Platform);
            this.computeContext = new ComputeContext(new[] { this.computeDevice }, properties, null, IntPtr.Zero);
            this.computeProgram = new ComputeProgram(this.computeContext, this.openCLSources);

            try
            {
                this.computeProgram.Build(new[] { this.computeDevice }, null, null, IntPtr.Zero);
            }

            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(computeProgram.GetBuildLog(this.computeDevice));
            }

            this.computeKernels = this.computeProgram.CreateAllKernels().ToList();
            this.computeKernel = this.computeKernels.First(k => k.FunctionName == openCLKernelFunction);
        }

        void DisposeOpenCLResources()
        {
            if (this.computeKernels != null)
            {
                this.computeKernels.ForEach(k => k.Dispose());
                this.computeKernels.Clear();
            }

            this.computeProgram?.Dispose();
            this.computeProgram = null;
            this.computeContext?.Dispose();
            this.computeContext = null;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        void Dispose(bool disposing)
        {
            if (isDisposed)
                return;

            if (disposing)
            {
                DisposeOpenCLResources();
            }

            isDisposed = true;
        }

        ~SpartacryptOpenCLMiner()
        {
            Dispose(false);
        }
    }
}
