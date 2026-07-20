namespace Spice86.Shared.Interfaces;

using Microsoft.Extensions.Logging;

/// <summary>
/// Logging service interface for the emulator, backed by Microsoft.Extensions.Logging.
/// </summary>
public interface ILoggerService : ILogger {
    /// <summary>
    /// A set of properties that will be inlined with every log statement.
    /// </summary>
    ILoggerPropertyBag LoggerPropertyBag { get; }

    /// <summary>
    /// Whether logs are silenced.
    /// </summary>
    bool AreLogsSilenced { get; set; }

    /// <summary>
    /// Sets the minimum log level.
    /// </summary>
    LogLevel MinimumLevel { get; set; }
}
