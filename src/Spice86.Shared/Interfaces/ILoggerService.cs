using Serilog.Core;

namespace Spice86.Shared.Interfaces;

using Serilog;

public interface ILoggerService : ILogger {
    /// <summary>
    /// Dynamic minimum log level, from Verbose to Fatal.
    /// </summary>
    LoggingLevelSwitch LogLevelSwitch { get; set; }
    
    /// <summary>
    /// Whether logs (except forced logs) are ignored.
    /// </summary>
    bool AreLogsSilenced { get; set; }
}
