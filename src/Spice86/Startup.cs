namespace Spice86; 

using Microsoft.Extensions.DependencyInjection;

using Serilog.Events;

using Spice86.Core.CLI;
using Spice86.DependencyInjection;
using Spice86.Shared.Interfaces;

internal static class Startup {
    public static ServiceProvider StartupInjectedServices(string[] commandLineArgs)
    {
        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        ServiceProvider serviceProvider = services.BuildServiceProvider();
        SetLoggingLevel(serviceProvider, commandLineArgs);
        return serviceProvider;
    }

    private static void SetLoggingLevel(ServiceProvider serviceProvider, string[] commandLineArgs) {
        ILoggerService? loggerService = serviceProvider.GetService<ILoggerService>();
        if (loggerService is null) {
            return;
        }
        Configuration configuration = CommandLineParser.ParseCommandLine(commandLineArgs);

        if (configuration.SilencedLogs)
        {
            loggerService.AreLogsSilenced = true;
        }
        else if (configuration.WarningLogs)
        {
            loggerService.LogLevelSwitch.MinimumLevel = LogEventLevel.Warning;
        }
        else if (configuration.VerboseLogs)
        {
            loggerService.LogLevelSwitch.MinimumLevel = LogEventLevel.Verbose;
        }
    }
}