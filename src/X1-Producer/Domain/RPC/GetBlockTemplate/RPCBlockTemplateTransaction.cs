namespace X1.Producer.Domain.RPC.GetBlockTemplate
{
    public class RPCBlockTemplateTransaction
    {
        public string data;
        public string txid;
        public string hash;
        public string[] depends;
        public long fee;
        public int sigops;
        public int weight;
    }
}