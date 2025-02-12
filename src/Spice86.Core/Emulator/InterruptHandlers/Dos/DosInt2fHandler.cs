namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// Reimplementation of int2f
/// </summary>
public class DosInt2fHandler : DosInterruptHandler {
    /// <summary>
    /// Initializes a new instance of the <see cref="DosInt2fHandler"/> class.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="cpu">The emulated CPU.</param>
    /// <param name="dosSwappableDataArea">The DOS structure holding global information, such as the INDOS flag.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosInt2fHandler(IMemory memory, Cpu cpu, DosSwappableDataArea dosSwappableDataArea, ILoggerService loggerService)
        : base(memory, cpu, dosSwappableDataArea, loggerService) {
        FillDispatchTable();
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x2f;

    /// <inheritdoc />
    public override void Run() {
        RunCriticalSection(() => {
            byte operation = State.AH;
            Run(operation);
        });
    }

    private void FillDispatchTable() {
        AddAction(0x16, () => ClearCFAndCX(true));
        AddAction(0x15, SendDeviceDriverRequest);
        AddAction(0x43, () => ClearCFAndCX(true));
        AddAction(0x46, () => ClearCFAndCX(true));
    }

    /// <summary>
    /// A service that does nothing, but set the carry flag to false, and CX to 0 to indicate success.
    /// <see href="https://github.com/FDOS/kernel/blob/master/kernel/int2f.asm"/> -> 'int2f_call:'.
    /// </summary>
    /// <param name="calledFromVm">Whether it was called by the emulator or not</param>
    public void ClearCFAndCX(bool calledFromVm) {
        SetCarryFlag(false, calledFromVm);
        State.CX = 0;
    }

    /// <summary>
    /// Sends a DOS device driver request. Always fails.
    /// TODO: Implement this.
    /// </summary>
    public void SendDeviceDriverRequest() {
        ushort drive = State.CX;
        uint deviceDriverRequestHeaderAddress = MemoryUtils.ToPhysicalAddress(State.ES, State.BX);
        if (LoggerService.IsEnabled(LogEventLevel.Debug)) {
            LoggerService.Debug("SEND DEVICE DRIVER REQUEST Drive {Drive} Request header at: {Address:x8}",
                drive, deviceDriverRequestHeaderAddress);
        }

        // Carry flag signals error.
        State.CarryFlag = true;
        // AX carries error reason.
        State.AX = 0x000F; // Error code for "Invalid drive"
    }
}