using Spice86.Core.DI;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Serilog;

using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.VM;
using Spice86.Logging;

/// <summary>
/// Reimplementation of int20
/// </summary>
public class DosInt20Handler : InterruptHandler {
    private readonly ILogger _logger;

    public DosInt20Handler(Machine machine, ILogger logger) : base(machine) {
        _logger = logger;
    }

    public override byte Index => 0x20;

    public override void Run() {
        _logger.Information("PROGRAM TERMINATE");
        _cpu.IsRunning = false;
    }
}