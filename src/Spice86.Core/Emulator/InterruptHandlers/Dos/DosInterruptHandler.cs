namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System;

public abstract class DosInterruptHandler : InterruptHandler {
    protected readonly DosSwappableDataArea _dosSwappableDataArea;
    protected DosInterruptHandler(IMemory memory, Cpu cpu, DosSwappableDataArea dosSwappableDataArea, ILoggerService loggerService) : base(memory, cpu, loggerService) {
        _dosSwappableDataArea = dosSwappableDataArea;
    }

    protected void RunCriticalSection(Action action) {
        _dosSwappableDataArea.EnterCriticalSection();
        action();
        _dosSwappableDataArea.LeaveCriticalSection();
    }
}
