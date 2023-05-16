namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Reimplementation of int20
/// </summary>
public class DosInt20Handler : InterruptHandler {
    private readonly ILoggerService _loggerService;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosInt20Handler(Machine machine, ILoggerService loggerService) : base(machine, loggerService) {
        _loggerService = loggerService;
    }

    /// <inheritdoc />
    public override byte Index => 0x20;

    /// <inheritdoc />
    public override void Run() {
        _loggerService.Verbose("PROGRAM TERMINATE");
        _cpu.IsRunning = false;
    }
}