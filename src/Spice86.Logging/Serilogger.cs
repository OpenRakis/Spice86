namespace Spice86.Logging;

using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Exceptions;

public static class Serilogger {
    private const string LogFormat = "[{Timestamp:HH:mm:ss} {Level:u3} {Properties}] {Message:lj}{NewLine}{Exception}";
    public static LoggingLevelSwitch LogLevelSwitch { get; set; } = new(LogEventLevel.Warning);

    private static readonly ILogger _loggerInstance;

    static Serilogger() {
        _loggerInstance = new LoggerConfiguration()
        .Enrich.WithExceptionDetails()
        .WriteTo.Console(outputTemplate: LogFormat)
        .WriteTo.Debug(outputTemplate: LogFormat)
        .MinimumLevel.ControlledBy(LogLevelSwitch)
        .CreateLogger();
    }

    public static ILogger Logger => _loggerInstance;

}
