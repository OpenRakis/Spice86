namespace Spice86.Logging;

using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Exceptions;

using Spice86.Shared.Interfaces;

public class LoggerService : ILoggerService {
    private const string LogFormat = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u4}] [{IP}] {Message:lj}{NewLine}{Exception}";

    private ILogger? _logger;

    private readonly LoggerConfiguration _loggerConfiguration;

    public LoggerService(ILoggerPropertyBag loggerPropertyBag) {
        LoggerPropertyBag = loggerPropertyBag;
        _loggerConfiguration = CreateLoggerConfiguration();
        _loggerConfiguration
            .MinimumLevel.ControlledBy(LogLevelSwitch);
    }
    public LoggingLevelSwitch LogLevelSwitch { get; set; } = new(LogEventLevel.Information);
    public bool AreLogsSilenced { get; set; }

    public ILoggerPropertyBag LoggerPropertyBag { get; }

    public LoggerConfiguration CreateLoggerConfiguration() {
        return new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.WithExceptionDetails()
            .WriteTo.Console(outputTemplate: LogFormat)
            .WriteTo.Debug(outputTemplate: LogFormat);
    }

    public ILoggerService WithLogLevel(LogEventLevel minimumLevel) {
        var logger = new LoggerService(LoggerPropertyBag) {LogLevelSwitch = new LoggingLevelSwitch(minimumLevel)};
        logger._loggerConfiguration.MinimumLevel.ControlledBy(new LoggingLevelSwitch(minimumLevel));
        return logger;
    }

    public void Write(LogEvent logEvent) {
        GetLogger().Write(logEvent);
    }

    public bool IsEnabled(LogEventLevel level) {
        return !AreLogsSilenced && level >= LogLevelSwitch.MinimumLevel;
    }

    /// <summary>
    ///     Creates the ILogger at the last possible time, since it can be created only once.
    /// </summary>
    /// <returns>The ILogger instance.</returns>
    private ILogger GetLogger() {
        _logger ??= _loggerConfiguration.CreateLogger();
        return AddProperties(_logger);
    }

    private ILogger AddProperties(ILogger logger) {
        return logger.ForContext("IP", $"{LoggerPropertyBag.CsIp}");
    }


#pragma warning disable Serilog004

    public void Information(string messageTemplate, params object?[]? properties) {
        GetLogger().Information(messageTemplate, properties);
    }

    public void Warning(string message) {
        GetLogger().Warning(message);
    }

    public void Warning(Exception? e, string messageTemplate, params object?[]? properties) {
        GetLogger().Warning(e, messageTemplate, properties);
    }

    public void Error(Exception? e, string messageTemplate, params object?[]? properties) {
        GetLogger().Error(e, messageTemplate, properties);
    }

    public void Fatal(Exception? e, string messageTemplate, params object?[]? properties) {
        GetLogger().Fatal(e, messageTemplate, properties);
    }

    public void Warning(string messageTemplate, params object?[]? properties) {
        GetLogger().Warning(messageTemplate, properties);
    }

    public void Error(string messageTemplate, params object?[]? properties) {
        GetLogger().Error(messageTemplate, properties);
    }

    public void Fatal(string messageTemplate, params object?[]? properties) {
        GetLogger().Fatal(messageTemplate, properties);
    }

    public void Debug(string messageTemplate, params object?[]? properties) {
        GetLogger().Debug(messageTemplate, properties);
    }

    public void Verbose(string messageTemplate, params object?[]? properties) {
        GetLogger().Verbose(messageTemplate, properties);
    }
#pragma warning restore Serilog004
}