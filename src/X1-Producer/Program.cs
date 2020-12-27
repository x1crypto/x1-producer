using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using X1.Producer.Services;

namespace X1.Producer
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                App.Init();

                App.ServiceProvider.GetService<WorkPuller>().Start().Wait();

                Console.CancelKeyPress += OnConsoleOnCancelKeyPress;

                while (true)
                    ListenForKeys();
            }
            catch (Exception e)
            {
                App.Logger.LogCritical(e.Message);
            }
        }

        static void ListenForKeys()
        {
            var cki = Console.ReadKey(true);
            switch (cki.KeyChar)
            {
                default:
                    App.Logger.LogWarning($"Unknown command '{cki.KeyChar}'");
                    break;
            }
        }

        static void OnConsoleOnCancelKeyPress(object s, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            App.ServiceProvider.GetService<IAppConfiguration>().Cts.Cancel();
            App.Logger.LogInformation("Waiting for shutdown...");

            while (true)
            {
                if (!App.ServiceProvider.GetService<WorkPuller>().HasShutDown)
                {
                    Task.Delay(500).Wait();
                    Console.Write(".");
                }
                else
                {
                    Console.WriteLine(Environment.NewLine);
                    break;
                }
            }

            App.Logger.LogInformation("Good bye!");
            Environment.Exit(0);
        }
    }
}
