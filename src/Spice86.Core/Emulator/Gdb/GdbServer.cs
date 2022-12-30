using Spice86.Core.DI;

namespace Spice86.Core.Emulator.Gdb;

using Serilog;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.VM;
using Spice86.Logging;

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

public sealed class GdbServer : IDisposable {
    private readonly ILogger _logger;
    private EventWaitHandle? _waitHandle;
    private readonly Configuration _configuration;
    private bool _disposed;
    private readonly Machine _machine;
    private bool _isRunning = true;
    private readonly Thread? _gdbServerThread;

    public GdbServer(Machine machine, ILogger logger, Configuration configuration) {
        _logger = logger;
        _machine = machine;
        _configuration = configuration;
        if (configuration.GdbPort is not null) {
            _gdbServerThread = new(RunServer) {
                Name = "GdbServer"
            };
            Start();
        }
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _gdbServerThread?.Join();
                _isRunning = false;
            }
            _disposed = true;
        }
    }

    public GdbCommandHandler? GdbCommandHandler { get; private set; }

    private void AcceptOneConnection(GdbIo gdbIo) {
        GdbCommandHandler gdbCommandHandler = new GdbCommandHandler(gdbIo,
            _machine,
            new ServiceProvider().GetLoggerForContext<GdbCommandHandler>(),
            _configuration);
        gdbCommandHandler.PauseEmulator();
        _waitHandle?.Set();
        GdbCommandHandler = gdbCommandHandler;
        while (gdbCommandHandler.IsConnected && gdbIo.IsClientConnected) {
            string command = gdbIo.ReadCommand();
            if (!string.IsNullOrWhiteSpace(command)) {
                gdbCommandHandler.RunCommand(command);
            }
        }
        _logger.Information("Client disconnected");
    }

    private void RunServer() {
        if (_configuration.GdbPort is null) {
            return;
        }
        int port = _configuration.GdbPort.Value;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("Starting GDB server");
        }
        try {
            while (_isRunning) {
                try {
                    using GdbIo gdbIo = new GdbIo(port,
                        new ServiceProvider().GetLoggerForContext<GdbIo>());
                    AcceptOneConnection(gdbIo);
                } catch (IOException e) {
                    e.Demystify();
                    _logger.Error(e, "Error in the GDB server, restarting it...");
                }
            }
        } catch (Exception e) {
            e.Demystify();
            _logger.Error(e, "Unhandled error in the GDB server, restarting it...");
        } finally {
            _machine.Cpu.IsRunning = false;
            _machine.MachineBreakpoints.PauseHandler.RequestResume();
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _logger.Information("GDB server stopped");
            }
        }
    }

    private void Start() {
        _gdbServerThread?.Start();
        // wait for thread to start
        _waitHandle = new AutoResetEvent(false);
        _waitHandle.WaitOne(Timeout.Infinite);
        _waitHandle.Dispose();
    }
}