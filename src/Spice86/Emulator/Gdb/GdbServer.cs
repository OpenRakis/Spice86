namespace Spice86.Emulator.Gdb;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Serilog;
using Spice86.Emulator.Machine;

public class GdbServer : IDisposable
{
    private static readonly ILogger _logger = Log.Logger.ForContext<GdbServer>();
    private Machine machine;
    private bool running = true;
    private volatile bool started = false;
    private string defaultDumpDirectory;
    private bool disposedValue;

    public async Task<GdbServer> CreateAsync(Machine machine, int port, string defaultDumpDirectory)
    {
        var server = new GdbServer(machine, defaultDumpDirectory);
        await StartAsync(port);
        return server;
    }

    private GdbServer(Machine machine, string defaultDumpDirectory)
    {
        this.machine = machine;
        this.defaultDumpDirectory = defaultDumpDirectory;
    }

    private async Task StartAsync(int port)
    {
        // wait for thread to start
        await RunServerAsync(port);
    }

    private async Task RunServerAsync(int port)
    {
        _logger.Information("Starting GDB server");
        try
        {
            while (running)
            {
                try
                {
                    var gdbIo = new GdbIo(port);
                    AcceptOneConnection(gdbIo);
                }
                catch (IOException e)
                {
                    _logger.Error(e, "Error in the GDB server, restarting it...");
                }
                await Task.Yield();
            }
        }
        finally
        {
            machine.GetCpu().SetRunning(false);
            machine.GetMachineBreakpoints().GetPauseHandler().RequestResume();
            _logger.Information("GDB server stopped");
        }
    }

    private void AcceptOneConnection(GdbIo gdbIo)
    {
        GdbCommandHandler gdbCommandHandler = new GdbCommandHandler(gdbIo, machine, defaultDumpDirectory);
        gdbCommandHandler.PauseEmulator();
        this.started = true;
        while (gdbCommandHandler.IsConnected())
        {
            string command = gdbIo.ReadCommand();
            if (string.IsNullOrWhiteSpace(command) == false)
            {
                gdbCommandHandler.RunCommand(command);
            }
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                //TODO: Dispose managed resources here
                running = false;
            }
            disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
