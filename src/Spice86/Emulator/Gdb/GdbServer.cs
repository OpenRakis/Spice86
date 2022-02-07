namespace Spice86.Emulator.Gdb;

using Serilog;

using Spice86.Emulator.VM;

using System;
using System.ComponentModel;
using System.IO;
using System.Linq.Expressions;

public class GdbServer : IDisposable {
    private static readonly ILogger _logger = Log.Logger.ForContext<GdbServer>();
    private string? defaultDumpDirectory;
    private bool disposedValue;
    private Machine machine;
    private bool running = true;
    private volatile bool started;

    public GdbServer(Machine machine, int port, string? defaultDumpDirectory) {
        this.machine = machine;
        this.defaultDumpDirectory = defaultDumpDirectory;
        Start(port);
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected void Dispose(bool disposing) {
        if (!disposedValue) {
            if (disposing) {
                running = false;
            }
            disposedValue = true;
        }
    }

    private void AcceptOneConnection(GdbIo gdbIo) {
        GdbCommandHandler gdbCommandHandler = new GdbCommandHandler(gdbIo, machine, defaultDumpDirectory);
        gdbCommandHandler.PauseEmulator();
        this.started = true;
        while (gdbCommandHandler.IsConnected() && gdbIo.IsClientConnected()) {
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
            while (running) {
                try {
                    using GdbIo gdbIo = new GdbIo(port);
                    AcceptOneConnection(gdbIo);
                } catch (IOException e) {
                    _logger.Error(e, "Error in the GDB server, restarting it...");
                }
            }
        } catch (Exception e) {
            _logger.Error(e, "Unhandled error in the GDB server, restarting it...");
        } finally {
            machine.GetCpu().SetRunning(false);
            machine.GetMachineBreakpoints().GetPauseHandler().RequestResume();
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
        while (!started) ;
    }
}