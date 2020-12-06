namespace XDS.Producer.Domain.RPC.GetBlockTemplate
{
    public class RPCBlockTemplate
    {
        public string longpollid;

        public int version;

        public string previousblockhash;

        public string bits;

        public string target;

        public int height;

        public uint curtime;

        public uint mintime; // = mediantimepast + 1

        public string[] mutable;

        public string[] rules;

        public string[] capabilities;

        public string default_witness_commitment;

        public RPCBlockTemplateTransaction[] transactions;

        public long coinbasevalue;

        public string stakemodifierv2;

        public string posbits;

        public string postarget;

        public string previousblockconsensus; // pindexPrev->IsProofOfStake() ? "pos" : "pow");

        public uint previousblocktime; // result.pushKV("previousblocktime", (int64_t) pindexPrev->GetBlockTime());

        public long ExtraNonce = 0;

        public int TemplateNumber;

    }
}
