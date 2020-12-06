using System;
using Microsoft.Extensions.DependencyInjection;

namespace XDS.Producer.Services
{
    /// <summary>
    /// Provides a reference to the ServiceProvider that can be used from within referenced projects that
    /// cannot use the static App.ServiceProvider.
    /// </summary>
    public interface IServiceLocator
    {
        IServiceCollection ServiceCollection { get; }
        IServiceProvider ServiceProvider { get; }
        void AddServiceProvider(IServiceProvider serviceProvider);
    }
}