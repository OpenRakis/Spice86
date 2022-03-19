namespace Spice86.Emulator.Gdb;

using Serilog;

using Spice86.Emulator.VM;

using System;
using System.ComponentModel;
using System.IO;

public class GdbServer : IDisposable {
    private static readonly ILogger _logger = Program.Logger.ForContext<GdbServer>();
    private readonly Configuration _configuration;
    private bool _disposedValue;
    private readonly Machine _machine;
    private bool _isRunning = true;
    private volatile bool _started;

    public GdbServer(Machine machine, Configuration configuration) {
        this._machine = machine;
        this._configuration = configuration;
        if (configuration.GdbPort != null) {
            Start(configuration.GdbPort.Value);
        }
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected void Dispose(bool disposing) {
        if (!_disposedValue) {
            if (disposing) {
                _isRunning = false;
            }
            _disposedValue = true;
        }
    }

    public GdbCommandHandler? GdbCommandHandler { get; private set; }

    private void AcceptOneConnection(GdbIo gdbIo) {
        var gdbCommandHandler = new GdbCommandHandler(gdbIo, _machine, _configuration);
        gdbCommandHandler.PauseEmulator();
        this._started = true;
        GdbCommandHandler = gdbCommandHandler;
        while (gdbCommandHandler.IsConnected && gdbIo.IsClientConnected) {
            string command = gdbIo.ReadCommand();
            if (string.IsNullOrWhiteSpace(command) == false) {
                gdbCommandHandler.RunCommand(command);
            }
        }
        _logger.Information("Client disconnected");
    }

    private void RunServer(int port) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("Starting GDB server");
        }
        try {
            while (_isRunning) {
                try {
                    using var gdbIo = new GdbIo(port);
                    AcceptOneConnection(gdbIo);
                } catch (IOException e) {
                    _logger.Error(e, "Error in the GDB server, restarting it...");
                }
            }
        } catch (Exception e) {
            _logger.Error(e, "Unhandled error in the GDB server, restarting it...");
        } finally {
            _machine.Cpu.IsRunning = false;
            _machine.MachineBreakpoints.PauseHandler.RequestResume();
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _logger.Information("GDB server stopped");
            }
        }
    }

    private void Start(int port) {
        using BackgroundWorker backgroundWorker = new();
        backgroundWorker.WorkerSupportsCancellation = false;
        backgroundWorker.DoWork += (s, e) => {
            RunServer(port);
        };
        backgroundWorker.RunWorkerAsync();
        // wait for thread to start
        while (!_started) {
            ;
        }
    }
}