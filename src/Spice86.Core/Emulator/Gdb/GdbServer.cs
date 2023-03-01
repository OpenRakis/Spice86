namespace Spice86.Core.Emulator.Gdb;

using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;
using System.Diagnostics;

public sealed class GdbServer : IDisposable {
    private readonly ILoggerService _loggerService;
    private EventWaitHandle? _waitFirstConnectionHandle;
    private readonly Configuration _configuration;
    private bool _disposed;
    private readonly Machine _machine;
    private bool _isRunning = true;
    private Thread? _gdbServerThread;
    private GdbIo? _gdbIo;

    public GdbServer(Machine machine, ILoggerService loggerService, Configuration configuration) {
        _loggerService = loggerService;
        _machine = machine;
        _configuration = configuration;
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

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

    public GdbCommandHandler? GdbCommandHandler { get; private set; }

    private void AcceptOneConnection(GdbIo gdbIo) {
        gdbIo.WaitForConnection();
        GdbCommandHandler gdbCommandHandler = new GdbCommandHandler(gdbIo,
            _machine,
            _loggerService,
            _configuration);
        gdbCommandHandler.PauseEmulator();
        OnConnect();
        GdbCommandHandler = gdbCommandHandler;
        while (gdbCommandHandler.IsConnected && gdbIo.IsClientConnected) {
            string command = gdbIo.ReadCommand();
            if (!string.IsNullOrWhiteSpace(command)) {
                gdbCommandHandler.RunCommand(command);
            }
        }
        _loggerService.Information("Client disconnected");
    }

    private void RunServer() {
        if (_configuration.GdbPort is null) {
            return;
        }
        int port = _configuration.GdbPort.Value;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("Starting GDB server");
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
                        _loggerService.Information("GDB Server connection closed and server is not running. Terminating it.");
                    }
                }
            }
        } catch (Exception e) {
            e.Demystify();
            _loggerService.Error(e, "Unhandled error in the GDB server, restarting it...");
        } finally {
            _machine.Cpu.IsRunning = false;
            _machine.MachineBreakpoints.PauseHandler.RequestResume();
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _loggerService.Information("GDB server stopped");
            }
        }
    }

    public void StartServerAndWait() {
        if (_configuration.GdbPort is not null) {
            StartServerThread();
        }
    }

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

    private void OnConnect() {
        _waitFirstConnectionHandle?.Set();
    }
}