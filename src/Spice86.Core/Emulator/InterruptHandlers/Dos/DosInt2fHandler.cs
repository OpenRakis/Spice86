namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// Reimplementation of int2f
/// </summary>
public class DosInt2fHandler : InterruptHandler {
    private readonly ExtendedMemoryManager? _xms;

    /// <summary>
    /// Initializes a new instance of the <see cref="DosInt2fHandler"/> class.
    /// </summary>
    /// <param name="xms">The extended memory manager. Can be <c>null</c> if XMS was not enabled.</param>
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosInt2fHandler(ExtendedMemoryManager? xms, IMemory memory,
        IFunctionHandlerProvider functionHandlerProvider, Stack stack,
        State state, ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _xms = xms;
        FillDispatchTable();
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x2f;

    /// <inheritdoc />
    public override void Run() {
        byte operation = State.AH;
        Run(operation);
    }

    private void FillDispatchTable() {
        AddAction(0x16, () => ClearCFAndCX(true));
        AddAction(0x15, SendDeviceDriverRequest);
        AddAction(0x43, () => GetXmsDriverInformation(true));
        AddAction(0x46, () => ClearCFAndCX(true));
    }

    private void GetXmsDriverInformation(bool calledFromVm) {
        switch (State.AL) {
            //Is XMS Driver installed
            case 0:
                State.AL = (byte)(_xms is null ? 0x0 : 0x80);
                break;
            //Get XMS Control Function Address
            case 0x10:
                State.ES = ExtendedMemoryManager.DosDeviceSegment;
                State.BX = 0x0;
                break;
            default:
                if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
                    LoggerService.Warning("{MethodName}: value {AL} not supported", nameof(GetXmsDriverInformation), State.AL);
                }
                break;
        }
        SetCarryFlag(false, calledFromVm);
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