namespace Spice86; 

using Microsoft.Extensions.DependencyInjection;

using Serilog.Events;

using Spice86.Core.CLI;
using Spice86.DependencyInjection;
using Spice86.Shared.Interfaces;

/// <summary>
/// Provides a method to initialize services and set the logging level based on command line arguments.
/// </summary>
public class Startup {
    private IServiceCollection _serviceCollection;

    public Startup(IServiceCollection serviceCollection) {
        _serviceCollection = serviceCollection;
    }

    /// <summary>
    /// Adds application-wide services to the services collection, sets the initial logging level, and returns the fully initialized <see cref="IServiceProvider"/>.
    /// </summary>
    /// <param name="commandLineArgs">The command line arguments.</param>
    /// <returns>A <see cref="IServiceProvider"/> instance that can be used to retrieve registered services.</returns>
    public IServiceProvider BuildServiceContainer(string[] commandLineArgs) {
        _serviceCollection.AddCmdLineParserAndLogging();
        IServiceProvider serviceProvider = _serviceCollection.BuildServiceProvider();
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
