namespace Spice86.Logging;

using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Exceptions;
using Spice86.Shared.Interfaces;

/// <inheritdoc/>
public class LoggerService : ILoggerService {
    /// <summary>
    /// The format for the log message that will be output.
    /// </summary>
    private const string LogFormat = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u4}] [{IP:j}] {Message:lj}{NewLine}{Exception}";
    
    /// <inheritdoc/>
    public LoggingLevelSwitch LogLevelSwitch { get; set; } = new(LogEventLevel.Warning);
    
    /// <inheritdoc/>
    public bool AreLogsSilenced { get; set; }

    private ILogger? _logger;

    private readonly LoggerConfiguration _loggerConfiguration;

    /// <inheritdoc/>
    public ILoggerPropertyBag LoggerPropertyBag { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LoggerService"/> class.
    /// </summary>
    /// <param name="loggerPropertyBag">The logger property bag.</param>
    public LoggerService(ILoggerPropertyBag loggerPropertyBag) {
        LoggerPropertyBag = loggerPropertyBag;
        _loggerConfiguration = CreateLoggerConfiguration();
        _loggerConfiguration
            .MinimumLevel.ControlledBy(LogLevelSwitch);
    }
    
    /// <summary>
    /// Creates the ILogger at the last possible time, since it can be created only once.
    /// </summary>
    /// <returns>The ILogger instance.</returns>
    private ILogger GetLogger() {
        _logger ??= _loggerConfiguration.CreateLogger();
        return AddProperties(_logger);
    }
    
    private ILogger AddProperties(ILogger logger) {
        return logger.ForContext("IP", LoggerPropertyBag.CsIp, destructureObjects: false);
    }
    
    /// <inheritdoc/>
    public LoggerConfiguration CreateLoggerConfiguration() {
        return new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.WithExceptionDetails()
            .WriteTo.Console(outputTemplate: LogFormat)
            .WriteTo.Debug(outputTemplate: LogFormat);
    }

    /// <inheritdoc/>
    public ILoggerService WithLogLevel(LogEventLevel minimumLevel) {
        var logger = new LoggerService(LoggerPropertyBag) {LogLevelSwitch = new LoggingLevelSwitch(minimumLevel)};
        logger._loggerConfiguration.MinimumLevel.ControlledBy(new LoggingLevelSwitch(minimumLevel));
        return logger;
    }

#pragma warning disable Serilog004
    
    /// <inheritdoc/>
    public void Information(string messageTemplate, params object?[]? properties) {
        if (AreLogsSilenced) {
            return;
        }
        GetLogger().Information(messageTemplate, properties);
    }

    /// <inheritdoc/>
    public void Warning(string message) {
        if (AreLogsSilenced) {
            return;
        }
        GetLogger().Warning(message);
    }
    
    /// <inheritdoc/>
    public void Warning(Exception? e, string messageTemplate, params object?[]? properties) {
        if (AreLogsSilenced) {
            return;
        }
        GetLogger().Warning(e, messageTemplate, properties);
    }
    
    /// <inheritdoc/>
    public void Error(Exception? e, string messageTemplate, params object?[]? properties) {
        if (AreLogsSilenced) {
            return;
        }
        GetLogger().Error(e, messageTemplate, properties);
    }
    
    /// <inheritdoc/>
    public void Fatal(Exception? e, string messageTemplate, params object?[]? properties) {
        if (AreLogsSilenced) {
            return;
        }
        GetLogger().Fatal(e, messageTemplate, properties);
    }
    
    /// <inheritdoc/>
    public void Warning(string messageTemplate, params object?[]? properties) {
        if (AreLogsSilenced) {
            return;
        }
        GetLogger().Warning(messageTemplate, properties);
    }
    
    /// <inheritdoc/>
    public void Error(string messageTemplate, params object?[]? properties) {
        if (AreLogsSilenced) {
            return;
        }
        GetLogger().Error(messageTemplate, properties);
    }
    
    /// <inheritdoc/>
    public void Fatal(string messageTemplate, params object?[]? properties) {
        if (AreLogsSilenced) {
            return;
        }
        GetLogger().Fatal(messageTemplate, properties);
    }
    
    /// <inheritdoc/>
    public void Debug(string messageTemplate, params object?[]? properties) {
        if (AreLogsSilenced) {
            return;
        }
        GetLogger().Debug(messageTemplate, properties);
    }
    
    /// <inheritdoc/>
    public void Verbose(string messageTemplate, params object?[]? properties) {
        if (AreLogsSilenced) {
            return;
        }
        GetLogger().Verbose(messageTemplate, properties);
    }
#pragma warning restore Serilog004

    /// <inheritdoc/>
    public void Write(LogEvent logEvent) {
        GetLogger().Write(logEvent);
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogEventLevel level) {
        _logger ??= _loggerConfiguration.CreateLogger();
        return _logger.IsEnabled(level);
    }
}
