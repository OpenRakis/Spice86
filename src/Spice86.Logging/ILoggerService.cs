namespace Spice86.Logging;

using Serilog;
using Serilog.Core;
public interface ILoggerService {
    LoggingLevelSwitch LogLevelSwitch { get; set; }
    ILogger Logger { get; }
}
