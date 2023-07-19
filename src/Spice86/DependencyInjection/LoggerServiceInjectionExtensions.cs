using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Spice86.Logging;
using Spice86.Shared.Interfaces;

namespace Spice86.DependencyInjection;

/// <summary>
/// Provides extension methods to register the logging services in the DI container.
/// </summary>
public static class LoggerServiceInjectionExtensions {
    /// <summary>
    /// Adds the logging services to the DI container.
    /// </summary>
    /// <param name="services">The service collection to add the services to.</param>
    /// <returns>The modified service collection.</returns>
    public static IServiceCollection AddLogging(this IServiceCollection services) {
        services.TryAddSingleton<ILoggerPropertyBag, LoggerPropertyBag>();
        services.TryAddSingleton<ILoggerService, LoggerService>();
        return services;
    }
}
