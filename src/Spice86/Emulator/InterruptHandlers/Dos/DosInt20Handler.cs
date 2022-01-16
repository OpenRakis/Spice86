namespace Spice86.Emulator.InterruptHandlers.Dos;

using Serilog;

using Spice86.Emulator.Machine;

/// <summary>
/// Reimplementation of int20
/// </summary>
public class DosInt20Handler : InterruptHandler {
    private static readonly ILogger _logger = Log.Logger.ForContext<DosInt20Handler>();

    public DosInt20Handler(Machine machine) : base(machine) {
    }

    public override int GetIndex() {
        return 0x20;
    }

    public override void Run() {
        _logger.Information("PROGRAM TERMINATE");
        _cpu.SetRunning(false);
    }
}