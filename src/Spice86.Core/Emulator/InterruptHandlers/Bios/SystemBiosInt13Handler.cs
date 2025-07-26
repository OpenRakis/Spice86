namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
///     INT 13h handler. BIOS disk access functions.
/// </summary>
/// <remarks>
/// In DOSBox, this is INT13_DiskHandler in bios_disk.cpp
/// </remarks>
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
        AddAction(0x4, () => VerifySectors(true));
        AddAction(0x15, () => GetDisketteOrHddType(true));
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

    /// <summary>
    /// Verify Disk Sector. This is a stub.
    /// </summary>
    /// <remarks>
    /// Shangai II needs this to run.
    /// </remarks>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void VerifySectors(bool calledFromVm) {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("BIOS INT13H: Verify Sectors 0x(AL:X2)", State.AL);
        }
        if(State.AL == 0) {
            State.AH = 0x1;
            SetCarryFlag(true, calledFromVm);
            return;
        }
        State.AH = 0x0;
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// Get Diskette Type or Check Hard Drive Installed
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void GetDisketteOrHddType(bool calledFromVm) {
        if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
            LoggerService.Warning("{Method} was called! Only returning fake" +
                "hard drive value if asking for first hard drive." +
                "Invalid drive otherwise.", nameof(GetDisketteOrHddType));
        }
        if(State.DL is 0x80) { // first hard disk drive
            State.AL = 0x3; // hard drive type
            State.CX = 3;  // High word of 32-bit sector count
            State.DX = 0x4800; //105 megs (0x00034800 = 215,040 of 512 bytes sector = 105 megs)
            SetCarryFlag(false, calledFromVm);
        } else {
            State.AH = 0xFF; // BIOS Disk error code: sense operation failed.
            SetCarryFlag(true, calledFromVm);
        }
    }
}