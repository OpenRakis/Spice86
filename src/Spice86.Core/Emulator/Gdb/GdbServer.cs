namespace Spice86.Core.Emulator.Gdb;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Function.Dump;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

/// <summary>
/// A GDB server that allows for remote debugging of the emulator.
/// </summary>
public sealed class GdbServer : IDisposable {
    private readonly ILoggerService _loggerService;
    private readonly Configuration _configuration;
    private bool _disposed;
    private bool _isRunning = true;
    private Thread? _gdbServerThread;
    private GdbIo? _gdbIo;
    private readonly IFunctionHandlerProvider _functionHandlerProvider;
    private readonly IPauseHandler _pauseHandler;
    private readonly IMemory _memory;
    private readonly State _state;
    private readonly IExecutionDumpFactory _executionDumpFactory;
    private readonly FunctionCatalogue _functionCatalogue;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;

    private readonly MemoryDataExporter _memoryDataExporter;
    private readonly DumpFolderMetadata _dumpContext;

    /// <summary>
    /// Creates a new instance of the GdbServer class with the specified parameters.
    /// </summary>
    /// <param name="configuration">The Configuration object that contains the settings for the GDB server.</param>
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="memoryDataExporter">The class used to dump main memory data properly.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="functionCatalogue">List of all functions.</param>
    /// <param name="executionDumpFactory">The class that dumps machine code execution flow.</param>
    /// <param name="emulatorBreakpointsManager">The class that handles breakpoints.</param>
    /// <param name="pauseHandler">The class used to support pausing/resuming the emulation via GDB commands.</param>
    /// <param name="dumpContext">The context containing program hash and dump directory information.</param>
    /// <param name="loggerService">The ILoggerService implementation used to log messages.</param>
    public GdbServer(Configuration configuration, IMemory memory,
        IFunctionHandlerProvider functionHandlerProvider,
        State state, MemoryDataExporter memoryDataExporter, FunctionCatalogue functionCatalogue,
        IExecutionDumpFactory executionDumpFactory,
        EmulatorBreakpointsManager emulatorBreakpointsManager, IPauseHandler pauseHandler,
        DumpFolderMetadata dumpContext, ILoggerService loggerService) {
        _loggerService = loggerService;
        _pauseHandler = pauseHandler;
        _memoryDataExporter = memoryDataExporter;
        _functionCatalogue = functionCatalogue;
        _state = state;
        _memory = memory;
        _executionDumpFactory = executionDumpFactory;
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _configuration = configuration;
        _functionHandlerProvider = functionHandlerProvider;
        _dumpContext = dumpContext;
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
                _gdbServerThread?.Join();
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
        GdbCommandHandler gdbCommandHandler = new GdbCommandHandler(
            _memory, _functionHandlerProvider, _state, _memoryDataExporter, _pauseHandler,
            _emulatorBreakpointsManager, _executionDumpFactory, _functionCatalogue,
            gdbIo,
            _loggerService,
            _dumpContext);
        gdbCommandHandler.PauseEmulator();
        while (gdbCommandHandler.IsConnected && gdbIo.IsClientConnected()) {
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
        int port = _configuration.GdbPort;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("Starting GDB server on port {Port} ...", port);
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
                    if (_isRunning) {
                        _loggerService.Error(e, "Error in the GDB server, restarting it...");
                    } else {
                        // Error occurred while stopping the server.
                        _loggerService.Error(e, "GDB Server connection closed and server is not running. Terminating it");
                    }
                }
            }
        } catch (Exception e) {
            _loggerService.Error(e, "Unhandled error in the GDB server. Stopping it.");
        } finally {
            _state.IsRunning = false;
            _pauseHandler.Resume();
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _loggerService.Information("GDB server stopped");
            }
        }
    }

    /// <summary>
    /// Starts the GDB server.
    /// </summary>
    public void StartServer() {
        _gdbServerThread = new(RunServer) {
            Name = "GdbServer"
        };
        _gdbServerThread?.Start();
    }
}