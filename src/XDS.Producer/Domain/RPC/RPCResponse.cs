namespace XDS.Producer.Domain.RPC
{
    public class RPCResponse<T> where T: class
    {
        public T Result;

        public int Status;

        public string StatusText;
    }
}