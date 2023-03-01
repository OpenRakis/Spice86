using Microsoft.Extensions.DependencyInjection;

using Spice86.Logging;
using Spice86.Shared.Interfaces;

namespace Spice86.DependencyInjection; 

public static class LoggerServiceInjectionExtensions {
    public static IServiceCollection AddLogging(this IServiceCollection services) {
        services.AddSingleton<ILoggerService, LoggerService>();
        return services;
    }
}