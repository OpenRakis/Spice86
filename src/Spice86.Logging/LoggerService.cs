namespace Spice86.Logging;

using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Exceptions;
using Spice86.Shared.Interfaces;

public class LoggerService : ILoggerService {
    private const string LogFormat = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u4}] [{IP:j}] {Message:lj}{NewLine}{Exception}";
    public LoggingLevelSwitch LogLevelSwitch { get; set; } = new(LogEventLevel.Warning);
    public bool AreLogsSilenced { get; set; }

    private ILogger? _logger;

    private LoggerConfiguration _loggerConfiguration;

    public ILoggerPropertyBag LoggerPropertyBag { get; }

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
        return logger.ForContext("IP", $"{LoggerPropertyBag.CodeSegment:X4}:{LoggerPropertyBag.InstructionPointer:X4}");
    }
    
    public LoggerConfiguration CreateLoggerConfiguration() {
        return new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.WithExceptionDetails()
            .WriteTo.Console(outputTemplate: LogFormat)
            .WriteTo.Debug(outputTemplate: LogFormat);
    }
    
    public LoggerConfiguration Override(string source, LogEventLevel minimumLevel) {
        _loggerConfiguration.MinimumLevel.Override(source, new LoggingLevelSwitch(minimumLevel));
        return _loggerConfiguration;
    }

#pragma warning disable Serilog004
    
    public void Information(string messageTemplate, params object?[]? properties) {
        if (AreLogsSilenced) {
            return;
        }
        GetLogger().Information(messageTemplate, properties);
    }

    public void Warning(string message) {
        if (AreLogsSilenced) {
            return;
        }
        GetLogger().Warning(message);
    }
    
    public void Warning(Exception? e, string messageTemplate, params object?[]? properties) {
        if (AreLogsSilenced) {
            return;
        }
        GetLogger().Warning(e, messageTemplate, properties);
    }
    
    public void Error(Exception? e, string messageTemplate, params object?[]? properties) {
        if (AreLogsSilenced) {
            return;
        }
        GetLogger().Error(e, messageTemplate, properties);
    }
    
    public void Fatal(Exception? e, string messageTemplate, params object?[]? properties) {
        if (AreLogsSilenced) {
            return;
        }
        GetLogger().Fatal(e, messageTemplate, properties);
    }
    
    public void Warning(string messageTemplate, params object?[]? properties) {
        if (AreLogsSilenced) {
            return;
        }
        GetLogger().Warning(messageTemplate, properties);
    }
    
    public void Error(string messageTemplate, params object?[]? properties) {
        if (AreLogsSilenced) {
            return;
        }
        GetLogger().Error(messageTemplate, properties);
    }
    
    public void Fatal(string messageTemplate, params object?[]? properties) {
        if (AreLogsSilenced) {
            return;
        }
        GetLogger().Fatal(messageTemplate, properties);
    }
    
    public void Debug(string messageTemplate, params object?[]? properties) {
        if (AreLogsSilenced) {
            return;
        }
        GetLogger().Debug(messageTemplate, properties);
    }
    
    public void Verbose(string messageTemplate, params object?[]? properties) {
        if (AreLogsSilenced) {
            return;
        }
        GetLogger().Verbose(messageTemplate, properties);
    }
#pragma warning restore Serilog004

    public void Write(LogEvent logEvent) {
        GetLogger().Write(logEvent);
    }

    public bool IsEnabled(LogEventLevel level) {
        return GetLogger().IsEnabled(level);
    }
}
