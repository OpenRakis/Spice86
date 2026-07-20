namespace Spice86.Logging;

using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;

using Spice86.Shared.Interfaces;

/// <summary>
/// Logger service backed by Serilog through Microsoft.Extensions.Logging.
/// </summary>
public class LoggerService : ILoggerService, IDisposable {
    private const string LogFormat =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u4}] [{ContextIndex}/{IP:j}] {Message:lj}{NewLine}{Exception}";

    private readonly LoggingLevelSwitch _logLevelSwitch;
    private readonly SerilogLoggerProvider _serilogProvider;
    private readonly Microsoft.Extensions.Logging.ILogger _msLogger;
    private bool _disposed;
    private bool _areLogsSilenced;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoggerService"/> class.
    /// </summary>
    public LoggerService() {
        _logLevelSwitch = new LoggingLevelSwitch();
        LoggerPropertyBag = new LoggerPropertyBag();
        Serilog.Core.Logger serilogLogger = CreateSerilogLogger();
        _serilogProvider = new SerilogLoggerProvider(serilogLogger, dispose: true);
        _msLogger = _serilogProvider.CreateLogger("Spice86");
    }

    /// <inheritdoc/>
    public ILoggerPropertyBag LoggerPropertyBag { get; }

    /// <summary>
    /// Provides access to the underlying Serilog level switch for advanced scenarios.
    /// </summary>
    public LoggingLevelSwitch LogLevelSwitch => _logLevelSwitch;

    /// <inheritdoc/>
    public LogLevel MinimumLevel {
        get => _logLevelSwitch.MinimumLevel switch {
            LogEventLevel.Verbose => LogLevel.Trace,
            LogEventLevel.Debug => LogLevel.Debug,
            LogEventLevel.Information => LogLevel.Information,
            LogEventLevel.Warning => LogLevel.Warning,
            LogEventLevel.Error => LogLevel.Error,
            LogEventLevel.Fatal => LogLevel.Critical,
            _ => LogLevel.None
        };
        set => _logLevelSwitch.MinimumLevel = value switch {
            LogLevel.Trace => LogEventLevel.Verbose,
            LogLevel.Debug => LogEventLevel.Debug,
            LogLevel.Information => LogEventLevel.Information,
            LogLevel.Warning => LogEventLevel.Warning,
            LogLevel.Error => LogEventLevel.Error,
            LogLevel.Critical => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };
    }

    /// <inheritdoc/>
    public bool AreLogsSilenced {
        get => _areLogsSilenced;
        set {
            _areLogsSilenced = value;
            if (value) {
                _logLevelSwitch.MinimumLevel = (LogEventLevel)6;
            }
        }
    }

    /// <inheritdoc/>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
        _msLogger.Log(logLevel, eventId, state, exception, formatter);
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) {
        return _msLogger.IsEnabled(logLevel);
    }

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull {
        return _msLogger.BeginScope(state);
    }

    /// <inheritdoc/>
    public void Dispose() {
        if (!_disposed) {
            _disposed = true;
            _serilogProvider.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    private Serilog.Core.Logger CreateSerilogLogger() {
        LoggerConfiguration configuration = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.With(new LoggerPropertyBagEnricher(LoggerPropertyBag))
            .MinimumLevel.ControlledBy(_logLevelSwitch);
        configuration.WriteTo.Async(conf => conf.Console(outputTemplate: LogFormat));
        configuration.WriteTo.Async(conf2 => conf2.Debug(outputTemplate: LogFormat));
        configuration.WriteTo.Async(conf3 =>
            conf3.File("logs/log-.txt", outputTemplate: LogFormat, rollingInterval: RollingInterval.Day));
        return configuration.CreateLogger();
    }
}
