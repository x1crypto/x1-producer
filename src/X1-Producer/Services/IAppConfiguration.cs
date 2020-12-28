using System.Collections.Generic;
using System.IO;
using System.Threading;
using NBitcoin;

namespace X1.Producer.Services
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

        List<int> DisabledDeviceIndices { get; }
        BitcoinWitPubKeyAddress MineToAddress { get; set; }
    }
}
