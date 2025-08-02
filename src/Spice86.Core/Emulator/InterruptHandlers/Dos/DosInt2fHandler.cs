namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
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
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="xms">The extended memory manager. Can be <c>null</c> if XMS was not enabled.</param>
    public DosInt2fHandler(IMemory memory,
        IFunctionHandlerProvider functionHandlerProvider, Stack stack,
        State state, ILoggerService loggerService, ExtendedMemoryManager? xms = null)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _xms = xms;
        FillDispatchTable();
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x2f;

    /// <inheritdoc />
    public override void Run() {
        byte multiplexServiceId = State.AH;

        if (!HasRunnable(multiplexServiceId)) {
            if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
                LoggerService.Warning("Unhandled INT2F call: {Operation:X2}", multiplexServiceId);
            }
            //Moving on...
            return;
        }

        Run(multiplexServiceId);
    }

    private void FillDispatchTable() {
        AddAction(0x10, () => ShareRelatedServices(true));
        AddAction(0x16, () => DosVirtualMachineServices(true));
        AddAction(0x15, () => MscdexServices(true));
        AddAction(0x43, () => XmsServices(true));
        AddAction(0x46, () => WindowsVirtualMachineServices());
        AddAction(0x4A, () => HighMemoryAreaServices());
    }

    public void ShareRelatedServices(bool calledFromVm) {
        if(State.AX == 0x1000) {
            //report SHARE.EXE as installed...
            State.AL = 0xFF;
        }
    }

    public void XmsServices(bool calledFromVm) {
        switch (State.AL) {
            //Is XMS Driver installed
            case (byte)XmsInt2FFunctionsCodes.InstallationCheck:
                State.AL = (byte)(_xms is null ? 0x0 : 0x80);
                break;
            //Get XMS Control Function Address
            case (byte)XmsInt2FFunctionsCodes.GetCallbackAddress:
                SegmentedAddress segmentedAddress = _xms?.CallbackAddress ?? new(0,0);
                State.ES = segmentedAddress.Segment;
                State.BX = segmentedAddress.Offset;
                break;
            default:
                if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
                    LoggerService.Warning("{MethodName}: value {AL} not supported", nameof(XmsServices), State.AL);
                }
                break;
        }
        SetCarryFlag(false, calledFromVm);
    }

    public void HighMemoryAreaServices() {
        switch(State.AL) {
            case 0x1 or 0x2: // Query Free HMA Space or Allocate HMA Space
                State.BX = 0; // Number of bytes available / Amount allocated
                State.ES = 0xFFFF; // Location of HMA
                State.DI = 0xFFFF; // Amount of allocated HMA memory
                break;
            default:
                if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
                    LoggerService.Warning("Unhandled INT2F HMA subfunction: {AX:X2}", State.AX);
                }
                break;
        }
    }

    /// <summary>
    /// A service that does nothing, but set the carry flag to false, and CX to 0 to indicate success.
    /// <see href="https://github.com/FDOS/kernel/blob/master/kernel/int2f.asm"/> -> 'int2f_call:'.
    /// </summary>
    /// <param name="calledFromVm">Whether it was called by the emulator or not</param>
    public void DosVirtualMachineServices(bool calledFromVm) {
        SetCarryFlag(false, calledFromVm);
        State.CX = 0;
    }

    public void WindowsVirtualMachineServices() {
        switch(State.AL) {
            case 0x80: //MS Windows v3.0 - INSTALLATION CHECK {undocumented} (AX: 4680h)
                State.AX = 1; //We are not Windows, but plain ol' MS-DOS.
                break;
            default:
                if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
                    LoggerService.Warning("Unhandled INT2F Windows VM subfunction: {AX:X2}", State.AX);
                }
                break;
        }
    }

    /// <summary>
    /// Sends a DOS device driver request to MSCDEX. Always fails.
    /// TODO: Implement MSCDEX.
    /// </summary>
    public void MscdexServices(bool calledFromVm) {
        ushort drive = State.CX;
        uint deviceDriverRequestHeaderAddress = MemoryUtils.ToPhysicalAddress(State.ES, State.BX);
        if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
            LoggerService.Warning("SEND DEVICE DRIVER REQUEST Drive {Drive} Request header at: {Address:x8}",
                drive, deviceDriverRequestHeaderAddress);
        }

        SetCarryFlag(true, calledFromVm);
        // AX carries error reason.
        State.AX = 0x000F; // Error code for "Invalid drive"
    }
}