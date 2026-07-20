namespace Spice86.Logging;

using Serilog;
using Serilog.Core;
using Serilog.Events;

using Spice86.Shared.Interfaces;

using System.Text;

/// <summary>
/// A DOS output handler that buffers characters written by the emulated program and
/// flushes complete lines to a dedicated Serilog logger (writing to a separate log file).
/// </summary>
public sealed class DosOutputHandler : IDosOutputHandler, IDisposable {
    private const string DosOutputLogFormat = "{Message:lj}{NewLine}";
    private readonly Logger _logger;
    private readonly StringBuilder _lineBuffer = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DosOutputHandler"/> class.
    /// Creates a dedicated Serilog logger that writes DOS program output to the specified file.
    /// </summary>
    /// <param name="logFilePath">Path to the DOS output log file.</param>
    public DosOutputHandler(string logFilePath) {
        _logger = new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Information)
            .WriteTo.File(logFilePath, outputTemplate: DosOutputLogFormat,
                rollingInterval: RollingInterval.Infinite,
                flushToDiskInterval: TimeSpan.FromSeconds(1))
            .CreateLogger();
    }

    /// <inheritdoc />
    public void OnCharacterOutput(char character) {
        if (_disposed) {
            return;
        }
        if (character == '\n') {
            FlushLine();
        } else if (character == '\r') {
            // Ignore CR; we flush on LF.
        } else {
            _lineBuffer.Append(character);
        }
    }

    /// <inheritdoc cref="IDisposable.Dispose"/>
    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        FlushLine();
        _logger.Dispose();
    }

    private void FlushLine() {
        if (_lineBuffer.Length == 0) {
            return;
        }
        string line = _lineBuffer.ToString();
        _lineBuffer.Clear();
        _logger.Information("{DosOutput}", line);
    }
}
