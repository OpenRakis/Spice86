namespace Bufdio.Common;

/// <summary>
/// An interface for simple logging operations.
/// </summary>
public interface ILogger
{
    /// <summary>
    /// Logs an information message.
    /// </summary>
    /// <param name="message">A string represents log message.</param>
    void LogInfo(string message);

    /// <summary>
    /// Logs a warning message.
    /// </summary>
    /// <param name="message">A string represents log message.</param>
    void LogWarning(string message);

    /// <summary>
    /// Logs an error message.
    /// </summary>
    /// <param name="message">A string represents log message.</param>
    void LogError(string message);
}
