namespace Spice86.Core.Emulator.Gdb;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// A GDB server that allows for remote debugging of the emulator.
/// </summary>
public sealed class GdbServer : IDisposable {
    private readonly ILoggerService _loggerService;
    private EventWaitHandle? _waitFirstConnectionHandle;
    private readonly Configuration _configuration;
    private readonly GdbIo _gdbIo;

    private bool _acceptedConnection;
    private bool _disposed;
    private bool _isRunning = true;
    private Thread? _gdbServerThread;
    private readonly IPauseHandler _pauseHandler;
    private readonly State _state;
    private readonly GdbCommandHandler _gdbCommandHandler;

    /// <summary>
    /// Creates a new instance of the GdbServer class with the specified parameters.
    /// </summary>
    /// <param name="gdbIo">The class used to establish the GDB connection.</param>
    /// <param name="pauseHandler">The class used to support pausing/resuming the emulation via GDB commands.</param>
    /// <param name="gdbCommandHandler">The class that answers to custom GDB commands.</param>
    /// <param name="loggerService">The ILoggerService implementation used to log messages.</param>
    /// <param name="configuration">The Configuration object that contains the settings for the GDB server.</param>
    /// <param name="state">The CPU state.</param>
    public GdbServer(GdbIo gdbIo, State state, IPauseHandler pauseHandler, GdbCommandHandler gdbCommandHandler, ILoggerService loggerService, Configuration configuration) {
        _gdbIo = gdbIo;
        _loggerService = loggerService;
        _pauseHandler = pauseHandler;
        _state = state;
        _configuration = configuration;
        _gdbCommandHandler = gdbCommandHandler;
    }

    /// <inheritdoc />
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the GdbServer instance and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                // Prevent it from restarting when the connection is killed
                _isRunning = false;
                _gdbIo.Dispose();
                // Release lock if called before the first connection to gdb server has been done
                _waitFirstConnectionHandle?.Set();
                _waitFirstConnectionHandle?.Dispose();
                _gdbServerThread?.Join();
                _isRunning = false;
            }
            _disposed = true;
        }
    }

    /// <summary>
    /// Accepts a single connection to the GDB server and creates a new GdbCommandHandler instance to handle the connection.
    /// </summary>
    /// <param name="gdbIo">The GdbIo instance used to communicate with the GDB client.</param>
    private void AcceptOneConnection(GdbIo gdbIo) {
        gdbIo.WaitForConnection();
        _gdbCommandHandler.PauseEmulator();
        OnConnect();
        while (_gdbCommandHandler.IsConnected && gdbIo.IsClientConnected) {
            string command = gdbIo.ReadCommand();
            if (!string.IsNullOrWhiteSpace(command)) {
                _gdbCommandHandler.RunCommand(command);
            }
        }
        _loggerService.Verbose("Client disconnected");
    }

    /// <summary>
    /// Runs the GDB server and handles incoming connections.
    /// </summary>
    private void RunServer() {
        if (_configuration.GdbPort is null) {
            return;
        }
        int port = _configuration.GdbPort.Value;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("Starting GDB server");
        }
        try {
            while (_isRunning) {
                try {
                    if (_acceptedConnection) {
                        continue;
                    }
                    // Make GdbIo available for Dispose
                    AcceptOneConnection(_gdbIo);
                    _acceptedConnection = true;
                } catch (IOException e) {
                    if (_isRunning) {
                        _loggerService.Error(e, "Error in the GDB server, restarting it...");
                    } else {
                        _loggerService.Verbose("GDB Server connection closed and server is not running. Terminating it");
                    }
                }
            }
        } catch (Exception e) {
            _loggerService.Error(e, "Unhandled error in the GDB server, restarting it");
        } finally {
            _state.IsRunning = false;
            _pauseHandler.Resume();
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _loggerService.Information("GDB server stopped");
            }
        }
    }

    /// <summary>
    /// Starts the GDB server thread and waits for the initial connection to be made.
    /// </summary>
    public void StartServerAndWait() {
        if (_configuration.GdbPort is not null) {
            StartServerThread();
        }
    }

    /// <summary>
    /// Starts the GDB server thread and waits for the initial connection to be made.
    /// </summary>
    private void StartServerThread() {
        _waitFirstConnectionHandle = new AutoResetEvent(false);
        _gdbServerThread = new(RunServer) {
            Name = "GdbServer"
        };
        _gdbServerThread?.Start();
        // wait for thread to start and the initial connection to be made
        _waitFirstConnectionHandle.WaitOne();
        // Remove the handle so that we don't wait anymore
        _waitFirstConnectionHandle.Dispose();
        _waitFirstConnectionHandle = null;
    }

    /// <summary>
    /// Sets the auto-reset event for the initial connection.
    /// </summary>
    private void OnConnect() => _waitFirstConnectionHandle?.Set();

    /// <summary>
    /// Executes a single CPU instruction.
    /// </summary>
    public void StepInstruction() => _gdbCommandHandler.Step();
}