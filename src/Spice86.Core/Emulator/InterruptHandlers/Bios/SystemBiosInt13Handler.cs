namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
///     INT 13h handler. BIOS disk access functions.
/// </summary>
public class SystemBiosInt13Handler : InterruptHandler {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory"></param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="loggerService">The logging service implementation.</param>
    public SystemBiosInt13Handler(
        IMemory memory, IFunctionHandlerProvider functionHandlerProvider,
        Stack stack, State state,
        ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        FillDispatchTable();
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x13;

    /// <inheritdoc />
    public override void Run() {
        byte operation = State.AH;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("BIOS function: 0x{@Function:X2}", operation, State.AH);
        }

        if (!HasRunnable(operation)) {
            if (LoggerService.IsEnabled(LogEventLevel.Error)) {
                LoggerService.Error("BIOS DISK function not provided: {@Function}", operation);
            }
        }
        Run(operation);
    }

    private void FillDispatchTable() {
        AddAction(0x0, () => ResetDiskSystem(true));
    }

    /// <summary>
    /// Reset Disk System.
    /// </summary>
    /// <remarks>
    /// This does nothing and always clears CF.
    /// </remarks>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void ResetDiskSystem(bool calledFromVm) {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("BIOS INT13H: Reset Disk Number 0x(DiskNumber:X2)", State.DL);
        }
        SetCarryFlag(false, calledFromVm);
    }
}