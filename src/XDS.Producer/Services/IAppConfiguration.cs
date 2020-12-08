using NBitcoin;
using System.IO;
using System.Threading;

namespace XDS.Producer.Services
{
    public interface IAppConfiguration
    {
        CancellationTokenSource Cts { get; }

        string ClientId { get; }

        string RPCHost { get; }

        int RPCPort { get; }

        string RPCUser { get; }

        string RPCPassword { get; }

        DirectoryInfo DataDirRoot { get; }

        public bool Mine { get;  }

        public bool Stake { get;  }
        BitcoinWitPubKeyAddress MineToAddress { get; set; }
    }
}
