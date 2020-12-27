using System.IO;
using System.Threading;
using NBitcoin;

namespace X1.Producer.Services
{
    public class AppConfiguration : IAppConfiguration
    {
        public CancellationTokenSource Cts { get; } = new CancellationTokenSource();

        public DirectoryInfo DataDirRoot { get; internal set; }
        
        public string Passphrase { get; internal set; }

        public string ClientId { get; internal set; }

        public string RPCHost { get; internal set; }

        public int RPCPort { get; internal set; }

        public string RPCUser { get; internal set; }

        public string RPCPassword { get; internal set; }

        public bool Mine { get; internal set; }

        public bool Stake { get; internal set; }

        public BitcoinWitPubKeyAddress MineToAddress { get;  set; }
    }
}