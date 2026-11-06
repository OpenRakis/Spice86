using Microsoft.Extensions.Logging;
namespace Spice86.Core.Emulator.InterruptHandlers.Dos;


using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.InterruptHandlers.Mscdex;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Handles DOS multiplex interrupt <c>INT 2Fh</c> services used by DOS extenders,
/// memory managers, ANSI drivers, MSCDEX, and Windows compatibility checks.
/// </summary>
/// <remarks>
/// Supported <c>AH</c> services currently include:
/// <list type="bullet">
/// <item><description><c>10h</c>: SHARE.EXE installation check (<c>AL=00h</c> returns installed).</description></item>
/// <item><description><c>15h</c>: MSCDEX dispatcher passthrough.</description></item>
/// <item><description><c>16h</c>: DOS virtual machine service placeholder.</description></item>
/// <item><description><c>1Ah</c>: ANSI console installation check (<c>AL=00h</c> returns installed).</description></item>
/// <item><description><c>43h</c>: XMS installation check and callback address query.</description></item>
/// <item><description><c>46h</c>: Windows virtual machine installation check compatibility.</description></item>
/// <item><description><c>4Ah</c>: High Memory Area query/allocation compatibility response.</description></item>
/// </list>
/// </remarks>
public class DosInt2fHandler : InterruptHandler {
    private readonly ExtendedMemoryManager? _xms;
    private readonly Mscdex _mscdexService;

    /// <summary>DOS virtual machine multiplex services; logs and does nothing.
    /// </summary>
    public void ReportDosVirtualMachineService(ushort function) {
        //Do nothing,not even set CX or CF...
        if (LoggerService.IsEnabled(LogLevel.Warning)) {
            LoggerService.LogWarning("Unhandled INT2F DOS VM subfunction: {AL:X2}", function & 0xFF);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DosInt2fHandler"/> class.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="mscdexService">The MSCDEX handler owned by the <c>Dos</c> class.</param>
    /// <param name="xms">The extended memory manager. Can be <c>null</c> if XMS was not enabled.</param>
    public DosInt2fHandler(IMemory memory,
        IFunctionHandlerProvider functionHandlerProvider, Stack stack,
        State state, ILogger loggerService, Mscdex mscdexService,
        ExtendedMemoryManager? xms = null)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _xms = xms;
        _mscdexService = mscdexService;
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x2f;

    /// <inheritdoc />
    public override void Run() {
        byte multiplexServiceId = State.AH;

        if (!HasRunnable(multiplexServiceId)) {
            if (LoggerService.IsEnabled(LogLevel.Warning)) {
                LoggerService.LogWarning("Unhandled INT2F call: {Operation:X2}", multiplexServiceId);
            }
            //Moving on...
            return;
        }

        Run(multiplexServiceId);
    }


    /// <summary>SHARE.EXE multiplex services; returns 0xFF for the installation check,
    /// the input AL otherwise.</summary>
    public byte ShareExeServices(byte al) {
        if (al == 0x0) {
            //report SHARE.EXE as installed...
            return 0xFF;
        }
        if (LoggerService.IsEnabled(LogLevel.Warning)) {
            LoggerService.LogWarning("{MethodName}: value {AL:X2} not supported", nameof(ShareExeServices), al);
        }
        return al;
    }

    [InterruptService(0x10, Reads = CpuValues.Ah | CpuValues.Al, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void ShareExeServices() {
        State.AL = ShareExeServices(State.AL);
    }

    /// <summary>ANSI.SYS multiplex services; returns 0xFF for the installation check,
    /// the input AL otherwise.</summary>
    public byte AnsiConsoleServices(byte al) {
        if (al == 0x0) {
            //Our ANSI console driver is installed
            return 0xFF;
        }
        if (LoggerService.IsEnabled(LogLevel.Warning)) {
            LoggerService.LogWarning("{MethodName}: value {AL:X2} not supported", nameof(AnsiConsoleServices), al);
        }
        return al;
    }

    [InterruptService(0x1A, Reads = CpuValues.Ah | CpuValues.Al, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void AnsiConsoleServices() {
        State.AL = AnsiConsoleServices(State.AL);
    }

    /// <summary>XMS driver multiplex services; BX/ES keep their preserved values except
    /// on the callback-address query.</summary>
    public (byte al, ushort bx, ushort es, bool carryFlag) XmsServices(
        byte al, ushort preservedBx, ushort preservedEs) {
        switch (al) {
            //Is XMS Driver installed
            case (byte)XmsInt2FFunctionsCodes.InstallationCheck:
                return ((byte)(_xms is null ? 0x0 : 0x80), preservedBx, preservedEs, false);
            //Get XMS Control Function Address
            case (byte)XmsInt2FFunctionsCodes.GetCallbackAddress:
                SegmentedAddress segmentedAddress = _xms?.CallbackAddress ?? new(0, 0);
                return (al, segmentedAddress.Offset, segmentedAddress.Segment, false);
            default:
                if (LoggerService.IsEnabled(LogLevel.Warning)) {
                    LoggerService.LogWarning("{MethodName}: value {AL} not supported", nameof(XmsServices), al);
                }
                return (al, preservedBx, preservedEs, false);
        }
    }

    [InterruptService(0x43, Reads = CpuValues.Ah | CpuValues.Al, Writes = CpuValues.Al | CpuValues.Bx | CpuValues.Es | CpuValues.Carry, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void XmsServices(bool calledFromVm) {
        (State.AL, State.BX, State.ES, bool carryFlag) = XmsServices(State.AL, State.BX, State.ES);
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>High memory area multiplex services; outputs keep their preserved values
    /// except on the query/allocate sub-functions.</summary>
    public (ushort bx, ushort di, ushort es) HighMemoryAreaServices(
        byte al, ushort preservedBx, ushort preservedDi, ushort preservedEs) {
        if (al is 0x1 or 0x2) { // Query Free HMA Space or Allocate HMA Space
            return (0, 0xFFFF, A20Gate.SegmentStartOfHighMemoryArea);
        }
        if (LoggerService.IsEnabled(LogLevel.Warning)) {
            LoggerService.LogWarning("Unhandled INT2F HMA subfunction: {AL:X2}", al);
        }
        return (preservedBx, preservedDi, preservedEs);
    }

    [InterruptService(0x4A, Reads = CpuValues.Ah | CpuValues.Al, Writes = CpuValues.Bx | CpuValues.Es | CpuValues.Di, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void HighMemoryAreaServices() {
        (State.BX, State.DI, State.ES) = HighMemoryAreaServices(State.AL, State.BX, State.DI, State.ES);
    }

    /// <summary>
    /// A service that does nothing.
    /// </summary>
    [InterruptService(0x16, Reads = CpuValues.Ax, Writes = CpuValues.None, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void DosVirtualMachineServices() {
        ReportDosVirtualMachineService(State.AX);
    }

    /// <summary>Windows virtual machine multiplex services; returns 1 for the
    /// installation check, null (AX preserved) otherwise.</summary>
    public ushort? WindowsVirtualMachineServices(byte al) {
        if (al == 0x80) { //MS Windows v3.0 - INSTALLATION CHECK {undocumented} (AX: 4680h)
            return 1; //We are not Windows, but plain ol' MS-DOS.
        }
        if (LoggerService.IsEnabled(LogLevel.Warning)) {
            LoggerService.LogWarning("Unhandled INT2F Windows VM subfunction: {AL:X2}", al);
        }
        return null;
    }

    [InterruptService(0x46, Reads = CpuValues.Ah | CpuValues.Al, Writes = CpuValues.Ax, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void WindowsVirtualMachineServices() {
        if (WindowsVirtualMachineServices(State.AL) is { } ax) {
            State.AX = ax;
        }
    }

    /// <summary>
    /// Handles MSCDEX INT 2Fh AH=15h subfunctions. Delegates to <see cref="Mscdex"/>.
    /// </summary>
    [InterruptService(0x15, Unknown = true)]
    public void MscdexServices(bool calledFromVm) {
        if (_mscdexService.Dispatch()) {
            SetCarryFlag(true, calledFromVm);
        }
    }
}
