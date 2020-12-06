namespace XDS.Producer.Staking
{
    public sealed class StakingStatus
    {
        // Values since staking started
        public long StartedUtc;
        public int BlocksAccepted;
        public int BlocksNotAccepted;

        public int Exceptions;
        public string LastException = "no error";

        // Values in one slot
        public long ComputeTimeMs;
        public int KernelsFound;

        public int UnspentOutputs;

        public long Immature;
        public long Weight;
        public double NetworkWeight;
        public double WeightPercent;
        public int ExpectedTime;
        public int ActualTime;
    }
}
