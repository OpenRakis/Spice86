using Serilog.Core;

namespace Spice86.Shared.Interfaces;

using Serilog;
using Serilog.Events;

/// <inheritdoc/>
public interface ILoggerService : ILogger {
    /// <summary>
    /// Dynamic global minimum log level, from Verbose to Fatal.
    /// </summary>
    LoggingLevelSwitch LogLevelSwitch { get; set; }
    
    /// <summary>
    /// Whether logs (except forced logs) are ignored.
    /// </summary>
    bool AreLogsSilenced { get; set; }

    /// <summary>
    /// Returns a new <see cref="LoggerConfiguration"/> from which a new instance of <see cref="ILogger"/> can be created, with <see cref="LoggerConfiguration.CreateLogger"/>. <br/>
    /// This <see cref="LoggerConfiguration"/> will output to the standard console, and debug console.
    /// You can also add a Context to it, with <see cref="Logger.ForContext"/>, for example.
    /// It will be detached from this logger, which means: <br/>
    /// - <see cref="LogLevelSwitch"/> won't affect it. <br/>
    /// - Global logger methods such as <see cref="ILoggerService.Debug"/> won't use it.
    /// </summary>
    /// <returns>The new <see cref="LoggerConfiguration"/></returns>
    LoggerConfiguration CreateLoggerConfiguration();

    /// <summary>
    /// Forced logs. Ignores <see cref="LogLevelSwitch"/>.
    /// </summary>
    /// <param name="messageTemplate">The message string with custom properties names, such as {CustomVariableName}</param>
    /// <param name="properties">The properties that are serialized into the message template</param>
    void Forced(string messageTemplate, params object?[]? properties);

}
