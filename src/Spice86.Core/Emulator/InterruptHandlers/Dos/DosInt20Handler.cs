namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Reimplementation of int20
/// </summary>
public class DosInt20Handler : DosInterruptHandler {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="cpu">The emulated CPU.</param>
    /// <param name="dosSwappableDataArea">The DOS structure holding global information, such as the INDOS flag.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosInt20Handler(IMemory memory, Cpu cpu, DosSwappableDataArea dosSwappableDataArea, ILoggerService loggerService) :
        base(memory, cpu, dosSwappableDataArea, loggerService) {
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x20;

    /// <inheritdoc />
    public override void Run() {
        RunCriticalSection(() => {
            LoggerService.Verbose("PROGRAM TERMINATE");
            State.IsRunning = false;
        });
    }
}