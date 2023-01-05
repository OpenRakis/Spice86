namespace Spice86.Logging;

using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Exceptions;
using Serilog.Enrichers;

public class LoggerService : ILoggerService {
    private const string LogFormat = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3} {Properties:j}] {Message:lj}{NewLine}{Exception}";
    public LoggingLevelSwitch LogLevelSwitch { get; set; } = new(LogEventLevel.Warning);

    public LoggerService() {
        Logger = new LoggerConfiguration()
        .Enrich.With(new ThreadIdEnricher())
        .Enrich.WithExceptionDetails()
        .WriteTo.Console(outputTemplate: LogFormat)
        .WriteTo.Debug(outputTemplate: LogFormat)
        .MinimumLevel.ControlledBy(LogLevelSwitch)
        .CreateLogger();
    }

    public ILogger Logger { get; }
}
