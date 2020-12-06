using System;

namespace XDS.Producer.Mining
{
    public interface IMiningDevice : IDisposable
    {
        void AttachToDevice(object device);

        uint FindProofOfWork(byte[] header, byte[] bits, uint nonceStart, uint iterations, out long elapsedMilliseconds);
    }
}