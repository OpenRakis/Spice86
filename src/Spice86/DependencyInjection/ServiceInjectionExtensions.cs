namespace Spice86.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;

using Spice86.Core.CLI;
using Spice86.Logging;
using Spice86.Shared.Interfaces;

/// <summary>
/// Provides extension methods to register services in the DI container.
/// </summary>
public static class ServiceInjectionExtensions {
    /// <summary>
    /// Adds the command line parser service and the logging service to the DI container.
    /// </summary>
    /// <param name="services">The service collection to add the services to.</param>
    /// <returns>The modified service collection.</returns>
    public static IServiceCollection AddCmdLineParserAndLogging(this IServiceCollection services) {
        services.AddSingleton<ILoggerPropertyBag, LoggerPropertyBag>();
        services.AddSingleton<ICommandLineParser, CommandLineParser>();
        services.AddSingleton<ILoggerService, LoggerService>();
        return services;
    }
}
