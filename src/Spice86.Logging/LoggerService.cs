#pragma warning disable CA2254
namespace Spice86.Logging;

using Serilog;
using Serilog.Core;
using Serilog.Events;

using Spice86.Shared.Interfaces;

/// <inheritdoc cref="ILoggerService" />
public class LoggerService : ILoggerService {
    private const string LogFormat =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u4}] [{ContextIndex}/{IP:j}] {Message:lj}{NewLine}{Exception}";

    private static readonly object?[] EmptyProperties = [];

    private readonly LoggerConfiguration _loggerConfiguration;
    private Logger? _logger;
    private LoggingLevelSwitch _logLevelSwitch;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LoggerService" /> class.
    /// </summary>
    public LoggerService() {
        _logLevelSwitch = new LoggingLevelSwitch();
        LoggerPropertyBag = new LoggerPropertyBag();
        _loggerConfiguration = CreateLoggerConfiguration();
        _loggerConfiguration.MinimumLevel.ControlledBy(_logLevelSwitch);
    }

    /// <inheritdoc />
    public LoggingLevelSwitch LogLevelSwitch {
        get => _logLevelSwitch;
        set {
            _logLevelSwitch = value ?? throw new ArgumentNullException(nameof(value));
            _loggerConfiguration.MinimumLevel.ControlledBy(_logLevelSwitch);
            ResetLogger();
        }
    }

    /// <inheritdoc />
    public bool AreLogsSilenced { get; set; }

    /// <inheritdoc />
    public ILoggerPropertyBag LoggerPropertyBag { get; }

    /// <inheritdoc />
    public LoggerConfiguration CreateLoggerConfiguration() {
        LoggerConfiguration configuration = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.With(new LoggerPropertyBagEnricher(LoggerPropertyBag))
            .WriteTo.Async(conf => conf.Console(outputTemplate: LogFormat)
                .WriteTo.Async(conf2 => conf2.Debug(outputTemplate: LogFormat))
                .WriteTo.Async(conf3 =>
                    conf3.File("logs/log-.txt", outputTemplate: LogFormat, rollingInterval: RollingInterval.Day)));
        return configuration;
    }

    /// <inheritdoc />
    public ILoggerService WithLogLevel(LogEventLevel minimumLevel) {
        var logger = new LoggerService {
            LogLevelSwitch = {
                MinimumLevel = minimumLevel
            }
        };
        return logger;
    }

    public void Write(LogEventLevel level, string messageTemplate) {
        GetLoggerForLevel(level)?.Write(level, messageTemplate);
    }

    public void Write<T>(LogEventLevel level, string messageTemplate, T propertyValue) {
        GetLoggerForLevel(level)?.Write(level, messageTemplate, propertyValue);
    }

    public void Write<T0, T1>(LogEventLevel level, string messageTemplate, T0 propertyValue0, T1 propertyValue1) {
        GetLoggerForLevel(level)?.Write(level, messageTemplate, propertyValue0, propertyValue1);
    }

    public void Write<T0, T1, T2>(LogEventLevel level, string messageTemplate, T0 propertyValue0, T1 propertyValue1,
        T2 propertyValue2) {
        GetLoggerForLevel(level)
            ?.Write(level, messageTemplate, propertyValue0, propertyValue1, propertyValue2);
    }

    public void Write(LogEventLevel level, string messageTemplate, params object?[]? propertyValues) {
        GetLoggerForLevel(level)?.Write(level, messageTemplate, Normalize(propertyValues));
    }

    public void Write(LogEventLevel level, Exception? exception, string messageTemplate) {
        GetLoggerForLevel(level)?.Write(level, exception, messageTemplate);
    }

    public void Write<T>(LogEventLevel level, Exception? exception, string messageTemplate, T propertyValue) {
        GetLoggerForLevel(level)?.Write(level, exception, messageTemplate, propertyValue);
    }

    public void Write<T0, T1>(LogEventLevel level, Exception? exception, string messageTemplate, T0 propertyValue0,
        T1 propertyValue1) {
        GetLoggerForLevel(level)
            ?.Write(level, exception, messageTemplate, propertyValue0, propertyValue1);
    }

    public void Write<T0, T1, T2>(LogEventLevel level, Exception? exception, string messageTemplate, T0 propertyValue0,
        T1 propertyValue1, T2 propertyValue2) {
        GetLoggerForLevel(level)
            ?.Write(level, exception, messageTemplate, propertyValue0, propertyValue1, propertyValue2);
    }

    public void Write(LogEventLevel level, Exception? exception, string messageTemplate,
        params object?[]? propertyValues) {
        GetLoggerForLevel(level)?.Write(level, exception, messageTemplate, Normalize(propertyValues));
    }

    /// <inheritdoc />
    public void Write(LogEvent logEvent) {
        GetLoggerForLevel(logEvent.Level)?.Write(logEvent);
    }

    /// <inheritdoc />
    public bool IsEnabled(LogEventLevel level) {
        return LogLevelSwitch.MinimumLevel <= level;
    }

    private void ResetLogger() {
        _logger?.Dispose();
        _logger = null;
    }

    private Logger? GetLoggerForLevel(LogEventLevel level) {
        if (AreLogsSilenced || !IsEnabled(level)) {
            return null;
        }

        _logger ??= _loggerConfiguration.CreateLogger();
        return _logger;
    }

    private static object?[] Normalize(object?[]? properties) {
        return properties is { Length: > 0 } ? properties : EmptyProperties;
    }

#pragma warning disable Serilog004
    public void Verbose(string messageTemplate) {
        GetLoggerForLevel(LogEventLevel.Information)?.Verbose(messageTemplate);
    }

    public void Verbose<T>(string messageTemplate, T propertyValue) {
        GetLoggerForLevel(LogEventLevel.Information)?.Verbose(messageTemplate, propertyValue);
    }

    public void Verbose<T0, T1>(string messageTemplate, T0 propertyValue0, T1 propertyValue1) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Verbose(messageTemplate, propertyValue0, propertyValue1);
    }

    public void Verbose<T0, T1, T2>(string messageTemplate, T0 propertyValue0, T1 propertyValue1, T2 propertyValue2) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Verbose(messageTemplate, propertyValue0, propertyValue1, propertyValue2);
    }

    public void Verbose(Exception? exception, string messageTemplate) {
        GetLoggerForLevel(LogEventLevel.Information)?.Verbose(exception, messageTemplate);
    }

    public void Verbose<T>(Exception? exception, string messageTemplate, T propertyValue) {
        GetLoggerForLevel(LogEventLevel.Information)?.Verbose(exception, messageTemplate, propertyValue);
    }

    public void Verbose<T0, T1>(Exception? exception, string messageTemplate, T0 propertyValue0, T1 propertyValue1) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Verbose(exception, messageTemplate, propertyValue0, propertyValue1);
    }

    public void Verbose<T0, T1, T2>(Exception? exception, string messageTemplate, T0 propertyValue0, T1 propertyValue1,
        T2 propertyValue2) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Verbose(exception, messageTemplate, propertyValue0, propertyValue1, propertyValue2);
    }

    public void Verbose(Exception? exception, string messageTemplate, params object?[]? propertyValues) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Verbose(exception, messageTemplate, Normalize(propertyValues));
    }

    /// <inheritdoc />
    public void Verbose(string messageTemplate, params object?[]? properties) {
        GetLoggerForLevel(LogEventLevel.Information)?.Verbose(messageTemplate, Normalize(properties));
    }

    public void Debug(Exception? exception, string messageTemplate, params object?[]? propertyValues) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Debug(exception, messageTemplate, Normalize(propertyValues));
    }

    public void Debug(string messageTemplate) {
        GetLoggerForLevel(LogEventLevel.Information)?.Debug(messageTemplate);
    }

    public void Debug<T>(string messageTemplate, T propertyValue) {
        GetLoggerForLevel(LogEventLevel.Information)?.Debug(messageTemplate, propertyValue);
    }

    public void Debug<T0, T1>(string messageTemplate, T0 propertyValue0, T1 propertyValue1) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Debug(messageTemplate, propertyValue0, propertyValue1);
    }

    public void Debug<T0, T1, T2>(string messageTemplate, T0 propertyValue0, T1 propertyValue1, T2 propertyValue2) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Debug(messageTemplate, propertyValue0, propertyValue1, propertyValue2);
    }

    public void Debug(Exception? exception, string messageTemplate) {
        GetLoggerForLevel(LogEventLevel.Information)?.Debug(exception, messageTemplate);
    }

    public void Debug<T>(Exception? exception, string messageTemplate, T propertyValue) {
        GetLoggerForLevel(LogEventLevel.Information)?.Debug(exception, messageTemplate, propertyValue);
    }

    public void Debug<T0, T1>(Exception? exception, string messageTemplate, T0 propertyValue0, T1 propertyValue1) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Debug(exception, messageTemplate, propertyValue0, propertyValue1);
    }

    public void Debug<T0, T1, T2>(Exception? exception, string messageTemplate, T0 propertyValue0, T1 propertyValue1,
        T2 propertyValue2) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Debug(exception, messageTemplate, propertyValue0, propertyValue1, propertyValue2);
    }

    /// <inheritdoc />
    public void Debug(string messageTemplate, params object?[]? properties) {
        GetLoggerForLevel(LogEventLevel.Information)?.Debug(messageTemplate, Normalize(properties));
    }

    public void Information(Exception? exception, string messageTemplate) {
        GetLoggerForLevel(LogEventLevel.Information)?.Information(exception, messageTemplate);
    }

    public void Information<T>(Exception? exception, string messageTemplate, T propertyValue) {
        GetLoggerForLevel(LogEventLevel.Information)?.Information(exception, messageTemplate, propertyValue);
    }

    public void Information<T0, T1>(Exception? exception, string messageTemplate, T0 propertyValue0,
        T1 propertyValue1) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Information(exception, messageTemplate, propertyValue0, propertyValue1);
    }

    public void Information<T0, T1, T2>(Exception? exception, string messageTemplate, T0 propertyValue0,
        T1 propertyValue1,
        T2 propertyValue2) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Information(exception, messageTemplate, propertyValue0, propertyValue1, propertyValue2);
    }

    public void Information(Exception? exception, string messageTemplate, params object?[]? propertyValues) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Information(exception, messageTemplate, Normalize(propertyValues));
    }

    public void Information(string messageTemplate) {
        GetLoggerForLevel(LogEventLevel.Information)?.Information(messageTemplate);
    }

    public void Information<T>(string messageTemplate, T propertyValue) {
        GetLoggerForLevel(LogEventLevel.Information)?.Information(messageTemplate, propertyValue);
    }

    public void Information<T0, T1>(string messageTemplate, T0 propertyValue0, T1 propertyValue1) {
        GetLoggerForLevel(LogEventLevel.Information)?.Information(messageTemplate, propertyValue0, propertyValue1);
    }

    public void Information<T0, T1, T2>(string messageTemplate, T0 propertyValue0, T1 propertyValue1,
        T2 propertyValue2) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Information(messageTemplate, propertyValue0, propertyValue1, propertyValue2);
    }

    /// <inheritdoc />
    public void Information(string messageTemplate, params object?[]? properties) {
        GetLoggerForLevel(LogEventLevel.Information)?.Information(messageTemplate, Normalize(properties));
    }

    public void Warning<T>(string messageTemplate, T propertyValue) {
        GetLoggerForLevel(LogEventLevel.Information)?.Warning(messageTemplate, propertyValue);
    }

    public void Warning<T0, T1>(string messageTemplate, T0 propertyValue0, T1 propertyValue1) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Warning(messageTemplate, propertyValue0, propertyValue1);
    }

    public void Warning<T0, T1, T2>(string messageTemplate, T0 propertyValue0, T1 propertyValue1, T2 propertyValue2) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Warning(messageTemplate, propertyValue0, propertyValue1, propertyValue2);
    }

    public void Warning(Exception? exception, string messageTemplate) {
        GetLoggerForLevel(LogEventLevel.Information)?.Warning(exception, messageTemplate);
    }

    public void Warning<T>(Exception? exception, string messageTemplate, T propertyValue) {
        GetLoggerForLevel(LogEventLevel.Information)?.Warning(exception, messageTemplate, propertyValue);
    }

    public void Warning<T0, T1>(Exception? exception, string messageTemplate, T0 propertyValue0, T1 propertyValue1) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Warning(exception, messageTemplate, propertyValue0, propertyValue1);
    }

    public void Warning<T0, T1, T2>(Exception? exception, string messageTemplate, T0 propertyValue0, T1 propertyValue1,
        T2 propertyValue2) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Warning(exception, messageTemplate, propertyValue0, propertyValue1, propertyValue2);
    }

    /// <inheritdoc />
    public void Warning(string message) {
        GetLoggerForLevel(LogEventLevel.Information)?.Warning(message);
    }

    /// <inheritdoc />
    public void Warning(Exception? e, string messageTemplate, params object?[]? properties) {
        ILogger? logger = GetLoggerForLevel(LogEventLevel.Warning);
        if (logger is null) {
            return;
        }

        object?[] propertyValues = Normalize(properties);
        if (e is null) {
            logger.Warning(messageTemplate, propertyValues);
        } else {
            logger.Warning(e, messageTemplate, propertyValues);
        }
    }

    public void Error(string messageTemplate) {
        GetLoggerForLevel(LogEventLevel.Information)?.Error(messageTemplate);
    }

    public void Error<T>(string messageTemplate, T propertyValue) {
        GetLoggerForLevel(LogEventLevel.Information)?.Error(messageTemplate, propertyValue);
    }

    public void Error<T0, T1>(string messageTemplate, T0 propertyValue0, T1 propertyValue1) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Error(messageTemplate, propertyValue0, propertyValue1);
    }

    public void Error<T0, T1, T2>(string messageTemplate, T0 propertyValue0, T1 propertyValue1, T2 propertyValue2) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Error(messageTemplate, propertyValue0, propertyValue1, propertyValue2);
    }

    public void Error(Exception? exception, string messageTemplate) {
        GetLoggerForLevel(LogEventLevel.Information)?.Error(exception, messageTemplate);
    }

    public void Error<T>(Exception? exception, string messageTemplate, T propertyValue) {
        GetLoggerForLevel(LogEventLevel.Information)?.Error(exception, messageTemplate, propertyValue);
    }

    public void Error<T0, T1>(Exception? exception, string messageTemplate, T0 propertyValue0, T1 propertyValue1) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Error(exception, messageTemplate, propertyValue0, propertyValue1);
    }

    public void Error<T0, T1, T2>(Exception? exception, string messageTemplate, T0 propertyValue0, T1 propertyValue1,
        T2 propertyValue2) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Error(exception, messageTemplate, propertyValue0, propertyValue1, propertyValue2);
    }

    /// <inheritdoc />
    public void Error(Exception? e, string messageTemplate, params object?[]? properties) {
        ILogger? logger = GetLoggerForLevel(LogEventLevel.Error);
        if (logger is null) {
            return;
        }

        object?[] propertyValues = Normalize(properties);
        if (e is null) {
            logger.Error(messageTemplate, propertyValues);
        } else {
            logger.Error(e, messageTemplate, propertyValues);
        }
    }

    public void Fatal(string messageTemplate) {
        GetLoggerForLevel(LogEventLevel.Information)?.Fatal(messageTemplate);
    }

    public void Fatal<T>(string messageTemplate, T propertyValue) {
        GetLoggerForLevel(LogEventLevel.Information)?.Fatal(messageTemplate, propertyValue);
    }

    public void Fatal<T0, T1>(string messageTemplate, T0 propertyValue0, T1 propertyValue1) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Fatal(messageTemplate, propertyValue0, propertyValue1);
    }

    public void Fatal<T0, T1, T2>(string messageTemplate, T0 propertyValue0, T1 propertyValue1, T2 propertyValue2) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Fatal(messageTemplate, propertyValue0, propertyValue1, propertyValue2);
    }

    public void Fatal(Exception? exception, string messageTemplate) {
        GetLoggerForLevel(LogEventLevel.Information)?.Fatal(exception, messageTemplate);
    }

    public void Fatal<T>(Exception? exception, string messageTemplate, T propertyValue) {
        GetLoggerForLevel(LogEventLevel.Information)?.Fatal(exception, messageTemplate, propertyValue);
    }

    public void Fatal<T0, T1>(Exception? exception, string messageTemplate, T0 propertyValue0, T1 propertyValue1) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Fatal(exception, messageTemplate, propertyValue0, propertyValue1);
    }

    public void Fatal<T0, T1, T2>(Exception? exception, string messageTemplate, T0 propertyValue0, T1 propertyValue1,
        T2 propertyValue2) {
        GetLoggerForLevel(LogEventLevel.Information)
            ?.Fatal(exception, messageTemplate, propertyValue0, propertyValue1, propertyValue2);
    }

    /// <inheritdoc />
    public void Fatal(Exception? e, string messageTemplate, params object?[]? properties) {
        ILogger? logger = GetLoggerForLevel(LogEventLevel.Fatal);
        if (logger is null) {
            return;
        }

        object?[] propertyValues = Normalize(properties);
        if (e is null) {
            logger.Fatal(messageTemplate, propertyValues);
        } else {
            logger.Fatal(e, messageTemplate, propertyValues);
        }
    }

    /// <inheritdoc />
    public void Warning(string messageTemplate, params object?[]? properties) {
        GetLoggerForLevel(LogEventLevel.Information)?.Warning(messageTemplate, Normalize(properties));
    }

    /// <inheritdoc />
    public void Error(string messageTemplate, params object?[]? properties) {
        GetLoggerForLevel(LogEventLevel.Information)?.Error(messageTemplate, Normalize(properties));
    }

    /// <inheritdoc />
    public void Fatal(string messageTemplate, params object?[]? properties) {
        GetLoggerForLevel(LogEventLevel.Information)?.Fatal(messageTemplate, Normalize(properties));
    }
#pragma warning restore Serilog004
}