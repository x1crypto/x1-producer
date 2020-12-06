using System;
using XDS.Producer.Domain;

namespace XDS.Producer.Mining
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
