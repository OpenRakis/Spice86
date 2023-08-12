namespace Spice86;

using Serilog.Events;

using Spice86.Core.CLI;
using Spice86.Shared.Interfaces;

/// <summary>
/// Provides a method to set the initial logging level based on command line arguments.
/// </summary>
internal static class Startup {
    /// <summary>
    /// Sets the logging level based on the command line arguments.
    /// </summary>
    /// <param name="loggerService">The logger service to configure.</param>
    /// <param name="configuration">The emulator configuration.</param>
    public static void SetLoggingLevel(ILoggerService loggerService, Configuration configuration) {
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
