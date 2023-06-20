namespace Spice86; 

using Microsoft.Extensions.DependencyInjection;

using Serilog.Events;

using Spice86.Core.CLI;
using Spice86.DependencyInjection;
using Spice86.Shared.Interfaces;

/// <summary>
/// Provides a method to initialize services and set the logging level based on command line arguments.
/// </summary>
public static class Startup {
    /// <summary>
    /// Initializes the service collection and sets the logging level.
    /// </summary>
    /// <param name="commandLineArgs">The command line arguments.</param>
    /// <returns>A <see cref="ServiceProvider"/> instance that can be used to retrieve registered services.</returns>
    public static IServiceProvider StartupInjectedServices(string[] commandLineArgs) {
        ServiceCollection services = new();
        services.TryAddCmdLineParserAndLogging();
        ServiceProvider serviceProvider = services.BuildServiceProvider();
        SetLoggingLevel(serviceProvider.GetRequiredService<ICommandLineParser>(), serviceProvider.GetRequiredService<ILoggerService>(), commandLineArgs);
        return serviceProvider;
    }

    private static void SetLoggingLevel(ICommandLineParser commandLineParser, ILoggerService loggerService, string[] commandLineArgs) {
        Configuration configuration = commandLineParser.ParseCommandLine(commandLineArgs);

        if (configuration.SilencedLogs) {
            loggerService.AreLogsSilenced = true;
        }
        else if (configuration.WarningLogs) {
            loggerService.LogLevelSwitch.MinimumLevel = LogEventLevel.Warning;
        }
        else if (configuration.VerboseLogs) {
            loggerService.LogLevelSwitch.MinimumLevel = LogEventLevel.Verbose;
        }
    }
}
