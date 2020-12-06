using System.Collections.Concurrent;

namespace XDS.Producer.Mining
{
    public class PerfCounter
    {
        public readonly ConcurrentQueue<double> HashRates = new ConcurrentQueue<double>();
        public double LastHashRate;
        public string Message = "No information.";
        public int BlocksFound;
        public int Errors;
        public string LastError = "no error";
        public string DeviceName = "";
        public long DeviceBatchSize;
        public bool IsTuned;
    }
}
