namespace Spice86.Shared.Interfaces;

using Serilog;
using Serilog.Core;
using Serilog.Events;

using System.Diagnostics;

/// <inheritdoc/>
public interface ILoggerService : ILogger {
    /// <summary>
    /// Dynamic global minimum log level, from Verbose to Fatal.
    /// </summary>
    LoggingLevelSwitch LogLevelSwitch { get; set; }
    
    /// <summary>
    /// A set of properties that will be inlined with every log statement.
    /// </summary>
    ILoggerPropertyBag LoggerPropertyBag { get; }
    
    /// <summary>
    /// Whether logs are ignored.
    /// </summary>
    bool AreLogsSilenced { get; set; }

    /// <summary>
    /// Returns a new <see cref="LoggerConfiguration"/> from which a new instance of <see cref="ILogger"/> can be created, with <see cref="LoggerConfiguration.CreateLogger"/>. <br/>
    /// This <see cref="LoggerConfiguration"/> will output to the standard console, and debug console.
    /// It will be detached from this logger anyway, which means: <br/>
    /// - <see cref="LogLevelSwitch"/> won't affect it. <br/>
    /// - <see cref="AreLogsSilenced"/> will not affect it. <br/>
    /// - Global logger methods such as <see cref="Debug"/> won't use it.
    /// </summary>
    /// <returns>The new <see cref="LoggerConfiguration"/></returns>
    LoggerConfiguration CreateLoggerConfiguration();

    /// <summary>
    /// Returns a new <see cref="ILoggerService"/> with the specified minimum log level.
    /// </summary> 
    ILoggerService WithLogLevel(LogEventLevel minimumLevel);
}
