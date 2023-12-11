﻿namespace Spice86.Core.Emulator.Gdb;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Pause;
using Spice86.Shared.Interfaces;

using System.Diagnostics;

/// <summary>
/// A GDB server that allows for remote debugging of the emulator.
/// </summary>
public sealed class GdbServer : IDisposable {
    private readonly ILoggerService _loggerService;
    private EventWaitHandle? _waitFirstConnectionHandle;
    private readonly Configuration _configuration;
    private bool _disposed;
    private bool _isRunning = true;
    private Thread? _gdbServerThread;
    private GdbIo? _gdbIo;
    private readonly Cpu _cpu;
    private readonly PauseHandler _pauseHandler;
    private readonly IMemory _memory;
    private readonly State _state;
    private readonly CallbackHandler _callbackHandler;
    private readonly ExecutionFlowRecorder _executionFlowRecorder;
    private readonly FunctionHandler _functionHandler;
    private readonly MachineBreakpoints _machineBreakpoints;
    private readonly IGui? _gui;

    /// <summary>
    /// Creates a new instance of the GdbServer class with the specified parameters.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="pauseHandler">The class used to support pausing/resuming the emulation via GDB commands.</param>
    /// <param name="loggerService">The ILoggerService implementation used to log messages.</param>
    /// <param name="configuration">The Configuration object that contains the settings for the GDB server.</param>
    /// <param name="gui">The graphical user interface. Is <c>null</c> in headless mode.</param>
    /// <param name="cpu">The emulated CPU.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="callbackHandler">The class that stores callback instructions definitions.</param>
    /// <param name="functionHandler">The class that handles functions calls.</param>
    /// <param name="executionFlowRecorder">The class that records machine code exexcution flow.</param>
    /// <param name="machineBreakpoints">The class that handles breakpoints.</param>
    public GdbServer(IMemory memory, Cpu cpu, State state, CallbackHandler callbackHandler, FunctionHandler functionHandler, ExecutionFlowRecorder executionFlowRecorder, MachineBreakpoints machineBreakpoints, PauseHandler pauseHandler, ILoggerService loggerService, Configuration configuration, IGui? gui) {
        _loggerService = loggerService;
        _pauseHandler = pauseHandler;
        _functionHandler = functionHandler;
        _cpu = cpu;
        _state = state;
        _memory = memory;
        _callbackHandler = callbackHandler;
        _executionFlowRecorder = executionFlowRecorder;
        _machineBreakpoints = machineBreakpoints;
        _gui = gui;
        _configuration = configuration;
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
                _gdbIo?.Dispose();
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
    /// Gets the GdbCommandHandler instance associated with the current GdbServer instance.
    /// </summary>
    public GdbCommandHandler? GdbCommandHandler { get; private set; }

    /// <summary>
    /// Returns a value indicating whether the GdbCommandHandler instance is available.<br/>
    /// This means that we set a connection with GDB earlier, and we assume that GDB client is still connected.
    /// </summary>
    public bool IsGdbCommandHandlerAvailable => GdbCommandHandler is not null;

    /// <summary>
    /// Accepts a single connection to the GDB server and creates a new GdbCommandHandler instance to handle the connection.
    /// </summary>
    /// <param name="gdbIo">The GdbIo instance used to communicate with the GDB client.</param>
    private void AcceptOneConnection(GdbIo gdbIo) {
        gdbIo.WaitForConnection();
        GdbCommandHandler gdbCommandHandler = new GdbCommandHandler(
            _memory, _cpu, _state, _pauseHandler, _machineBreakpoints,
            _callbackHandler, _executionFlowRecorder, _functionHandler,
            gdbIo,
            _loggerService,
            _configuration,
            _gui);
        gdbCommandHandler.PauseEmulator();
        OnConnect();
        GdbCommandHandler = gdbCommandHandler;
        while (gdbCommandHandler.IsConnected && gdbIo.IsClientConnected) {
            string command = gdbIo.ReadCommand();
            if (!string.IsNullOrWhiteSpace(command)) {
                gdbCommandHandler.RunCommand(command);
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
                    using GdbIo gdbIo = new GdbIo(port, _loggerService);
                    // Make GdbIo available for Dispose
                    _gdbIo = gdbIo;
                    AcceptOneConnection(gdbIo);
                    _gdbIo = null;
                } catch (IOException e) {
                    e.Demystify();
                    if (_isRunning) {
                        _loggerService.Error(e, "Error in the GDB server, restarting it...");
                    } else {
                        _loggerService.Verbose("GDB Server connection closed and server is not running. Terminating it");
                    }
                }
            }
        } catch (Exception e) {
            e.Demystify();
            _loggerService.Error(e, "Unhandled error in the GDB server, restarting it");
        } finally {
            _state.IsRunning = false;
            _pauseHandler.RequestResume();
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
        // Remove the handle so that no wait
        _waitFirstConnectionHandle.Dispose();
        _waitFirstConnectionHandle = null;
    }

    /// <summary>
    /// Sets the auto-reset event for the initial connection.
    /// </summary>
    private void OnConnect() {
        _waitFirstConnectionHandle?.Set();
    }

    /// <summary>
    /// Executes a single CPU instruction.
    /// </summary>
    public void StepInstruction() {
        GdbCommandHandler?.Step();
    }
}