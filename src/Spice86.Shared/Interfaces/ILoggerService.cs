using Serilog.Core;

namespace Spice86.Shared.Interfaces;

using Serilog;
using Serilog.Configuration;
using Serilog.Events;

using System.Diagnostics;

/// <inheritdoc/>
public interface ILoggerService : ILogger {
    /// <summary>
    /// Dynamic global minimum log level, from Verbose to Fatal.
    /// </summary>
    LoggingLevelSwitch LogLevelSwitch { get; set; }
    
    /// <summary>
    /// Whether logs are ignored.
    /// </summary>
    bool AreLogsSilenced { get; set; }

    /// <summary>
    /// Returns a new <see cref="LoggerConfiguration"/> from which a new instance of <see cref="ILogger"/> can be created, with <see cref="LoggerConfiguration.CreateLogger"/>. <br/>
    /// This <see cref="LoggerConfiguration"/> will output to the standard console, and debug console.
    /// You can also add a Context to it, with <see cref="Logger.ForContext"/>, for example.
    /// It will be detached from this logger anyway, which means: <br/>
    /// - <see cref="LogLevelSwitch"/> won't affect it. <br/>
    /// - <see cref="AreLogsSilenced"/> will not affect it. <br/>
    /// - Global logger methods such as <see cref="Debug"/> won't use it.
    /// </summary>
    /// <returns>The new <see cref="LoggerConfiguration"/></returns>
    LoggerConfiguration CreateLoggerConfiguration();

    /// <summary>
    /// Override the minimum level for events from a specific namespace or type name.
    /// This API is not supported for configuring sub-loggers (created through <see cref="Logger"/>). Use <see cref="LoggerConfiguration.Filter"/> or <see cref="LoggerSinkConfiguration.Conditional(Func{LogEvent, bool}, Action{LoggerSinkConfiguration})"/> instead.
    /// You also might consider using https://github.com/serilog/serilog-filters-expressions.
    /// </summary>
    /// <param name="source">The (partial) namespace or type name to set the override for.</param>
    /// <param name="minimumLevel">The minimum level applied to loggers for matching sources.</param>
    /// <returns>Configuration object allowing method chaining.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="source"/> is <code>null</code></exception>
    LoggerConfiguration Override(string source, LogEventLevel minimumLevel);

}
