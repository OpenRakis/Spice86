namespace Spice86.Emulator.Gdb;

using Serilog;

using Spice86.Emulator.VM;

using System;
using System.IO;

public class GdbServer : IDisposable {
    private static readonly ILogger _logger = Log.Logger.ForContext<GdbServer>();
    private string? defaultDumpDirectory;
    private bool disposedValue;
    private Machine machine;
    private bool running = true;
    private bool started = false;

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
        while (gdbCommandHandler.IsConnected()) {
            string command = gdbIo.ReadCommand();
            if (string.IsNullOrWhiteSpace(command) == false) {
                gdbCommandHandler.RunCommand(command);
            }
        }
    }

    private void RunServer(int port) {
        _logger.Information("Starting GDB server");
        try {
            while (running) {
                try {
                    var gdbIo = new GdbIo(port);
                    AcceptOneConnection(gdbIo);
                } catch (IOException e) {
                    _logger.Error(e, "Error in the GDB server, restarting it...");
                }
            }
        } finally {
            machine.GetCpu().SetRunning(false);
            machine.GetMachineBreakpoints().GetPauseHandler().RequestResume();
            _logger.Information("GDB server stopped");
        }
    }

    private void Start(int port) {
        // wait for thread to start
        while (!started) {
            RunServer(port);
        }
    }
}