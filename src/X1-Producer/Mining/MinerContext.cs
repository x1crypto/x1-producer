using System;
using X1.Producer.Domain;

namespace X1.Producer.Mining
{
    public class MinerContext
    {
        public string Name;

        public uint StartNonce;
        public uint MaxNonce;

        public DateTime ThreadStartedUtc;
        public int ThreadIndex;
        public SlimBlock SlimBlock;
    }
}
