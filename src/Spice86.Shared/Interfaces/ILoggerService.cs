using Serilog.Core;
using Serilog.Events;

namespace Spice86.Shared.Interfaces;

public interface ILoggerService {
    LoggingLevelSwitch LogLevelSwitch { get; set; }
    bool AreLogsSilenced { get; set; }

    void Warning(string message);

    void Information(string messageTemplate, params object?[]? properties);

    void Warning(Exception e, string messageTemplate, params object?[]? properties);

    void Error(Exception e, string messageTemplate, params object?[]? properties);

    void Fatal(Exception e, string messageTemplate, params object?[]? properties);
    void Warning(string messageTemplate, params object?[]? properties);

    void Error(string messageTemplate, params object?[]? properties);

    void Fatal(string messageTemplate, params object?[]? properties);

    void Debug(string messageTemplate, params object?[]? properties);

    void Verbose(string messageTemplate, params object?[]? properties);

    bool IsEnabled(LogEventLevel level);
}
