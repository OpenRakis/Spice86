namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

public abstract class DosInterruptHandler : InterruptHandler {
    protected readonly DosSwappableDataArea _dosSwappableDataArea;
    protected DosInterruptHandler(IMemory memory, Cpu cpu, DosSwappableDataArea dosSwappableDataArea, ILoggerService loggerService)
        : base(memory, cpu, loggerService) {
        _dosSwappableDataArea = dosSwappableDataArea;
    }

    /// <summary>
    /// Increments the InDosFlag and runs the action, before decrementing the InDosFlag.
    /// </summary>
    /// <param name="action">The DOS kernel code to run.</param>
    protected void RunDosCriticalSection(Action action) {
        _dosSwappableDataArea.InDosFlag++;
        action();
        _dosSwappableDataArea.InDosFlag--;
    }
}
