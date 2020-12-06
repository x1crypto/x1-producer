using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using IniParser;
using IniParser.Model;
using IniParser.Parser;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDS.Producer.Domain.RPC;
using XDS.Producer.Mining;
using XDS.Producer.Services;
using XDS.Producer.Staking;
using XDS.Producer.State;

namespace XDS.Producer
{
    static class App
    {
        public static IServiceProvider ServiceProvider;

        public static ILogger Logger;

        public static void Init()
        {
            var dataDirRoot = SelectDataDirRoot();
            Configure();
            CreateLogger();
            Logger.LogInformation($"{GetVersionString()}");
            Logger.LogInformation($"Using data directory {dataDirRoot}");

            C.Network = NBitcoin.Altcoins.XDS.Instance.Mainnet; // for extension methods
            Logger.LogInformation($"Initialized network {C.Network.Name}.");
            LoadConfig(dataDirRoot);
        }

        public static IServiceCollection CreateServiceCollection()
        {
            return new ServiceCollection();
        }

        public static IServiceProvider CreateServiceProvider(IServiceCollection serviceCollection)
        {
            IServiceLocator serviceLocator = new ServiceLocator(serviceCollection);

            serviceCollection.AddSingleton(serviceLocator);

            ServiceProvider = serviceCollection.BuildServiceProvider();

            serviceLocator.AddServiceProvider(ServiceProvider);
            return ServiceProvider;
        }

        public static DirectoryInfo SelectDataDirRoot()
        {
            var di = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                ? new DirectoryInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XDS-Producer")) 
                : new DirectoryInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".xds-producer"));

            if(!di.Exists)
                di.Create();

            return di;
        }

        public static string GetVersionString()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(asm.Location);
            return $"{fvi.ProductName} v.{fvi.ProductVersion}";
        }

        internal static void UpdateNodeServices(string clientId, DirectoryInfo dataDirRoot, string passphrase, string rpcHost, int rpcPort, string rpcUsr, string rpcPassword, bool mine, bool stake)
        {
            var nodeServices = (AppConfiguration) App.ServiceProvider.GetService<IAppConfiguration>();
            nodeServices.ClientId = clientId;
            nodeServices.DataDirRoot = dataDirRoot;
            nodeServices.Passphrase = passphrase;
            nodeServices.RPCHost = rpcHost;
            nodeServices.RPCPort = rpcPort;
            nodeServices.RPCUser = rpcUsr;
            nodeServices.RPCPassword = rpcPassword;
            nodeServices.Stake = stake;
            nodeServices.Mine = mine;
        }

        public static void Configure()
        {
            IServiceCollection serviceCollection = App.CreateServiceCollection();
            AddRequiredServices(serviceCollection);
            CreateServiceProvider(serviceCollection);
        }

        public static void CreateLogger()
        {
            var loggerFactory = ServiceProvider.GetService<ILoggerFactory>();
            Logger = loggerFactory.CreateLogger<Program>();
        }

        static void AddRequiredServices(IServiceCollection services)
        {
            services.AddLogging(loggingBuilder => { loggingBuilder.AddConsole(); });

            services.AddSingleton<IAppConfiguration,AppConfiguration>();
            services.AddSingleton<StakingService>();

            services.AddSingleton<WorkPuller>();
            services.AddTransient<RPCClient>(); // use AddTransient, the RPCClient is not thread-safe

            services.AddSingleton<MinerCoordinator>();
            services.AddSingleton<BlockTemplateCache>();

            services.AddSingleton<DeviceController>();
        }

        static void LoadConfig(DirectoryInfo dataDirRoot)
        {
            var configFilePath = Path.Combine(dataDirRoot.ToString(), "xds-producer.config");

            if (!File.Exists(configFilePath))
                CreateConfigFileAndExit(configFilePath);

            try
            {
                var parser = new FileIniDataParser();
                parser.Parser.Configuration.CommentString = "#";
                parser.Parser.Configuration.AssigmentSpacer = "";

                IniData data = parser.ReadFile(configFilePath);
                string clientId = Environment.GetEnvironmentVariable("COMPUTERNAME") ??
                                  Environment.GetEnvironmentVariable("HOSTNAME");

                string targetIp = Read(data, "targetip");
                var ip = IPAddress.Parse(targetIp);
                var host = $"http://{ip}";


                string targetPort = Read(data, "targetport");
                bool mine = Read(data, "mine") == "0" ? false : Read(data, "mine") == "1" ? true : throw new InvalidOperationException("mine = 1 or mine = 0 is expected.");
                bool stake = Read(data, "stake") == "0" ? false : Read(data, "stake") == "1" ? true : throw new InvalidOperationException("stake = 1 or stake = 0 is expected.");
                string rpcuser = Read(data, "rpcuser");
                string rpcpassword = Read(data, "rpcpassword");
                App.UpdateNodeServices(clientId, dataDirRoot, null, host, int.Parse(targetPort), rpcuser, rpcpassword, mine, stake);
            }
            catch (Exception e)
            {
                App.Logger.LogCritical($"Error processing config: {e.Message}. Config file: {configFilePath}. Please fix the errors or delete the config file to create a new one. Press any key to exit.");
                Console.ReadKey(true);
                Environment.Exit(1);
            }
            Logger.LogInformation($"Configuration loaded from {configFilePath}.");
        }

        static string Read(IniData iniData, string key)
        {
            var value = iniData.GetKey(key);
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"Can't find key {key} in the configuration file.");
            return value.Trim();
        }

        static void CreateConfigFileAndExit(string configFilePath)
        {
            var parser = new IniDataParser {Configuration = {CommentString = "#", AssigmentSpacer = ""}};

            IniData parsedData = parser.Parse("");

            parsedData.Global.SetKeyData(new KeyData("rpcuser") { Comments = { "The rpcuser must be set and match the rpcuser of the node. Only US-ASCII characters are alllowed." } });
            parsedData.Global.SetKeyData(new KeyData("rpcpassword") { Comments = { "The rpcpassword must be set and match the rpcpassword of the node. Only US-ASCII characters are alllowed." } });

            parsedData.Global.SetKeyData(new KeyData("targetip")
            {
                Value = "127.0.0.1",
                Comments =
            {
                "The IP address of the node you want to connect to. Default: 127.0.0.1",
                "The node may require you set additional config parameters, e.g. server=1, rpcbind=0.0.0.0, rpcallowip=192.168.0.0/16. WARNING: Allowing RPC access is a security risk and even more so if you allow access from remote machines."
            }
            });
            parsedData.Global.SetKeyData(new KeyData("targetport") { Value = C.Network.RPCPort.ToString(), Comments = { $"The RPC TCP port of the node you want to connect to. Default: {C.Network.RPCPort}." } });

            parsedData.Global.SetKeyData(new KeyData("mine") { Value = "0", Comments = { "To mine, set the value to 1, otherwise to 0." } });
            parsedData.Global.SetKeyData(new KeyData("stake") { Value = "0", Comments = { "To stake, set the value to 1, otherwise to 0. Staking requires targetip=127.0.0.1, i.e. the wallet and xds-producer must run on the same machine, because the keys are retrieved via a temp file." } });

            var config = parsedData.ToString();

            File.WriteAllText(configFilePath, config);

            App.Logger.LogInformation($"No config file was found. Created a new default config file: {Environment.NewLine}" +
                                      $"{configFilePath}{Environment.NewLine}" +
                                      "Please review and complete the configuration and start the program again. Press any key to exit.");
            Console.ReadKey(true);

            Environment.Exit(0);

        }

    }
}
