using System;
using Microsoft.Extensions.DependencyInjection;

namespace X1.Producer.Services
{
    public sealed class ServiceLocator : IServiceLocator
    {
        public ServiceLocator(IServiceCollection serviceCollection)
        {
            this.ServiceCollection = serviceCollection;
        }

        public IServiceCollection ServiceCollection { get; }

        public IServiceProvider ServiceProvider { get; private set; }

        public void AddServiceProvider(IServiceProvider provider)
        {
            this.ServiceProvider = provider;
        }
    }
}
