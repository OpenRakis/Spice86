namespace Spice86.Core.Emulator.Devices;

using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Common implementation for device threads that:
/// - Loops until device is stopped
/// - Stops operations when emulator is paused
/// - Allows thread loop to be paused if device is inactive
/// </summary>
public class DeviceThread : IDisposable {
    private readonly ILoggerService _loggerService;
    private Thread? _thread;
    private readonly string _name;
    private volatile bool _deviceStopRequested;
    private readonly IPauseHandler _pauseHandler;
    private bool _disposed;
    private readonly Action _loopBody;
    // Start unblocked
    private readonly ManualResetEvent _manualResetEvent = new(false);

    /// <summary>
    /// Creates a new thread for devices to run in background.
    /// </summary>
    /// <param name="name">Name of the device</param>
    /// <param name="loopBody">Action to run repeatedly</param>
    /// <param name="pauseHandler">Emulator pause handler</param>
    /// <param name="loggerService">The logs</param>
    public DeviceThread(string name, Action loopBody, IPauseHandler pauseHandler, ILoggerService loggerService) {
        _name = name;
        _pauseHandler = pauseHandler;
        _loggerService = loggerService;
        _loopBody = loopBody;
    }

    /// <summary>
    /// Pauses the device loop
    /// </summary>
    public void Pause() {
        _manualResetEvent.Reset();
    }

    /// <summary>
    /// Resumes the run of the device loop
    /// </summary>
    public void Resume() {
        _manualResetEvent.Set();
    }

    /// <summary>
    /// Start the thread and the loop. A new thread is created there if needed.
    /// </summary>
    public void StartThreadIfNeeded() {
        if (_disposed || _thread != null || _deviceStopRequested) {
            return;
        }

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("Starting device thread '{ThreadName}'", _name);
        }
        _thread = new Thread(DeviceLoop) {
            Name = _name,
        };
        _thread.Start();
        Resume();
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("Device thread '{ThreadName}' started", _name);
        }
    }

    /// <summary>
    /// Exits the loop and stops the thread.
    /// </summary>
    public void StopThreadIfNeeded() {
        if (_thread == null) {
            return;
        }
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("Stopping device thread '{ThreadName}'", _name);
        }
        // Signal thread we want to stop
        _deviceStopRequested = true;
        Resume();
        if (_thread.IsAlive) {
            _thread.Join();
        }

        // Stopped, ready to go again
        _thread = null;
        _deviceStopRequested = false;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("Device thread '{ThreadName}' stopped", _name);
        }
    }

    private void DeviceLoop() {
        while (!_deviceStopRequested) {
            _pauseHandler.WaitIfPaused();
            _manualResetEvent.WaitOne(Timeout.Infinite);
            _loopBody.Invoke();
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
    }

    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                StopThreadIfNeeded();
                _manualResetEvent.Dispose();
            }

            _disposed = true;
        }
    }
}