using Microsoft.Extensions.Logging;
namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Enums;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.DeviceScheduler;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// Interrupt 15h is a ROM BIOS service that includes several extensions to the original PC ROM BIOS,
/// including the means to find out how much RAM (conventional plus extended) is on the system. <br/>
/// A program uses this service to find out how much extended memory there is.
/// </summary>
public class SystemBiosInt15Handler : InterruptHandler {
    private readonly A20Gate _a20Gate;
    private readonly Configuration _configuration;
    private readonly BiosDataArea _biosDataArea;
    private readonly IOPortDispatcher _ioPortDispatcher;
    private readonly DeviceScheduler _emulationLoopScheduler;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="configuration">The emulator configuration. This is what to run and how.</param>
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="a20Gate">The A20 line gate.</param>
    /// <param name="biosDataArea">The BIOS data area for accessing system flags and variables.</param>
    /// <param name="emulationLoopScheduler">Used for registering a timed callback named 'OnWaitComplete' when emulated programs call the BIOS WAIT function.</param>
    /// <param name="ioPortDispatcher">The I/O port dispatcher for accessing hardware ports (e.g., CMOS).</param>
    /// <param name="initializeResetVector">Whether to initialize the reset vector with a HLT instruction.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public SystemBiosInt15Handler(Configuration configuration, IMemory memory,
        IFunctionHandlerProvider functionHandlerProvider, Stack stack,
        State state, A20Gate a20Gate, BiosDataArea biosDataArea, DeviceScheduler emulationLoopScheduler,
        IOPortDispatcher ioPortDispatcher, ILogger loggerService, bool initializeResetVector)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _a20Gate = a20Gate;
        _emulationLoopScheduler = emulationLoopScheduler;
        _configuration = configuration;
        _biosDataArea = biosDataArea;
        _ioPortDispatcher = ioPortDispatcher;
        if (initializeResetVector) {
            memory.UInt16[0xF000, 0xFFF0] = 0xF4;
        }
    }

    /// <summary>
    /// INT 15h, AH=50h - DOS/V Font Subsystem Access.
    /// Retrieves the address of a font read or write function for Japanese DOS/V systems.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by the CPU or not.</param>
    /// <remarks>
    /// <b>Inputs:</b><br/>
    /// AH = 50h<br/>
    /// AL = function type (00h = read font, 01h = write font)<br/>
    /// BL = 00h (must be zero)<br/>
    /// BH = character size (00h = single-byte, 01h = double-byte)<br/>
    /// DH = width of character cell<br/>
    /// DL = height of character cell<br/>
    /// BP = code page (0 = default, 437 = US English, 932 = Japanese, 934 = Korea, 936 = China, 938 = Taiwan)<br/>
    /// <b>Outputs:</b><br/>
    /// CF clear if successful:<br/>
    ///   AH = 00h<br/>
    ///   ES:BX = address of requested function<br/>
    /// CF set on error:<br/>
    ///   AH = error code (01h = invalid font type, 02h = BL not zero, 03h = invalid font size,
    ///   04h = invalid code page, 80h = unsupported on PC, 86h = unsupported on XT)<br/>
    /// </remarks>
    /// <summary>Reports the DOS/V font subsystem as unsupported on PC AT.</summary>
    public (byte ah, bool carryFlag) DosVFontSubsystemAccess() {
        //Makes Knights of Xentar work on MS-DOS (Western port for IBM PC)
        return (0x80, true); //Unsupported function (PC AT)
    }

    [InterruptService(0x50, Reads = CpuValues.Ah, Writes = CpuValues.Ah | CpuValues.Carry, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void DosVFontSubsystemAccess(bool calledFromVm) {
        (State.AH, bool carryFlag) = DosVFontSubsystemAccess();
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x15;

    /// <inheritdoc />
    public override void Run() {
        byte operation = State.AH;
        Run(operation);
    }

    /// <summary>Sets up or cancels the RTC wait interval; AL=01h cancels, otherwise the
    /// flag at <paramref name="callback"/> is set after
    /// <paramref name="microseconds"/>. AH is preserved unless a wait is already in
    /// progress.</summary>
    public (byte? ah, bool carryFlag) WaitFunction(byte operation, uint microseconds,
        SegmentedAddress callback) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("INT 15h, AH=83h - WAIT FUNCTION, AL={AL:X2}", operation);
        }

        if (operation == 0x01) {
            _biosDataArea.RtcWaitFlag = 0;
            ModifyCmosRegister(Devices.Cmos.CmosRegisterAddresses.StatusRegisterB, value => (byte)(value & ~0x40));

            if (LoggerService.IsEnabled(LogLevel.Trace)) {
                LoggerService.LogTrace("WAIT FUNCTION cancelled");
            }
            return (null, false);
        }

        if (_biosDataArea.RtcWaitFlag != 0) {
            if (LoggerService.IsEnabled(LogLevel.Warning)) {
                LoggerService.LogWarning("WAIT FUNCTION called while event already in progress");
            }
            return (0x80, true);
        }

        _biosDataArea.UserWaitCompleteFlag = callback;
        _biosDataArea.UserWaitTimeout = microseconds;
        _biosDataArea.RtcWaitFlag = 1;
        ModifyCmosRegister(Devices.Cmos.CmosRegisterAddresses.StatusRegisterB, value => (byte)(value | 0x40));

        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("WAIT FUNCTION set: count={Count} microseconds, callback={Segment:X4}:{Offset:X4}",
                microseconds, callback.Segment, callback.Offset);
        }
        return (null, false);
    }

    /// <summary>
    /// INT 15h, AH=83h - Event Wait Interval.
    /// Sets up RTC periodic interrupt for timed delay. ES:BX points to a flag byte whose bit 7 will be set when the wait completes.
    /// AL=00h to set, AL=01h to cancel. Returns CF=1 + AH=80h if event already in progress.
    /// </summary>
    [InterruptService(0x83, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Cx | CpuValues.Dx | CpuValues.Es | CpuValues.Bx, Writes = CpuValues.Ah | CpuValues.Carry, Memory = ServiceMemoryEffect.Write, Io = true, ExecutionPath = ServiceExecutionPath.Direct)]
    public void WaitFunction(bool calledFromVm) {
        (byte? ah, bool carryFlag) = WaitFunction(State.AL,
            ((uint)State.CX << 16) | State.DX, new SegmentedAddress(State.ES, State.BX));
        if (ah is { } status) {
            State.AH = status;
        }
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>Toggles or queries the A20 gate per the AL sub-function; null outputs
    /// are preserved (all of them for an unrecognized command).</summary>
    public (byte? al, byte? ah, ushort? bx, bool? carryFlag) ToggleA20GateOrGetStatus(byte operation) {
        switch (operation) {
            case 0:
                _a20Gate.IsEnabled = false;
                return (null, null, null, false);
            case 1:
                _a20Gate.IsEnabled = true;
                return (null, null, null, false);
            case 2:
                return ((byte)(_a20Gate.IsEnabled ? 0x1 : 0x0), 0, null, false);
            case 3:
                _a20Gate.IsEnabled = false;
                return (null, 0, 3, false); //Bitmask, keyboard and 0x92

            default:
                if (LoggerService.IsEnabled(LogLevel.Error)) {
                    LoggerService.LogError("Unrecognized command in AL for {MethodName}",
                        nameof(ToggleA20GateOrGetStatus));
                }
                return (null, null, null, null);
        }
    }

    /// <summary>
    /// Bios support function for the A20 Gate line. <br/>
    /// AL contains one of:<br/>
    /// <ul><br/>
    ///   <li>0: Disable</li><br/>
    ///   <li>1: Enable</li><br/>
    ///   <li>2: Query status</li><br/>
    ///   <li>3: Get A20 support</li><br/>
    /// </ul>
    /// </summary>
    [InterruptService(0x24, Reads = CpuValues.Ah | CpuValues.Al, Writes = CpuValues.Al | CpuValues.Ah | CpuValues.Bx | CpuValues.Carry, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void ToggleA20GateOrGetStatus(bool calledFromVm) {
        (byte? al, byte? ah, ushort? bx, bool? carryFlag) =
            ToggleA20GateOrGetStatus(State.AL);
        if (al is { } alValue) {
            State.AL = alValue;
        }
        if (ah is { } ahValue) {
            State.AH = ahValue;
        }
        if (bx is { } bxValue) {
            State.BX = bxValue;
        }
        if (carryFlag is { } flag) {
            SetCarryFlag(flag, calledFromVm);
        }
    }

    /// <summary>Returns the contiguous extended memory size in KB above 1MB.</summary>
    public (ushort ax, bool carryFlag) GetExtendedMemorySize() {
        ushort ax = _a20Gate.IsEnabled || _configuration.Xms is true
            ? (ushort)0 //Either the HMA is not accessible, or the DOS driver protects it.
            : (ushort)(A20Gate.EndOfHighMemoryArea - A20Gate.StartOfHighMemoryArea);
        return (ax, false);
    }

    /// <summary>
    /// Reports  number of contiguous KB starting at absolute address 0x100000 <br/>
    /// CF is cleared if successful, set otherwise. <br/> <br/>
    /// Error is 0x80 (invalid command) for the IBM PC, Tandy, and PC Junior platforms. <br/>
    /// Error is 0x86 (function not supported) for the IBM PC XT, and IBM PS/2.
    /// </summary>
    /// <remarks>
    /// TSRs which wish to allocate extended memory to themselves often hook
    /// this call, and return a reduced memory size.
    /// They are then free to use the memory between the new and old sizes at will. <br/><br/>
    /// The standard BIOS only returns memory between 1MB and 16MB; use AH=0xC7 for memory beyond 16MB.
    /// </remarks>
    [InterruptService(0x88, Reads = CpuValues.Ah, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void GetExtendedMemorySize(bool calledFromVm) {
        (State.AX, bool carryFlag) = GetExtendedMemorySize();
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>Copies <paramref name="wordCount"/> words in extended memory using the GDT
    /// at <paramref name="gdtSegment"/>:<paramref name="gdtOffset"/>; status in AH with
    /// CF.</summary>
    public (byte ah, bool carryFlag) CopyExtendedMemory(ushort gdtSegment, ushort gdtOffset, ushort wordCount) {
        // Save current A20 state and enable it for extended memory access
        bool prevA20Enable = _a20Gate.IsEnabled;
        _a20Gate.IsEnabled = true;

        uint byteCount = (uint)wordCount * 2;

        // Validate word count first
        if (wordCount == 0) {
            _a20Gate.IsEnabled = prevA20Enable;
            return ((byte)ExtendedMemoryCopyStatus.SourceCopiedIntoDest, false);
        }

        // Maximum 128K transfer on 386+ (following SeaBIOS comment)
        if (wordCount > 0x8000) {
            _a20Gate.IsEnabled = prevA20Enable;
            return ((byte)ExtendedMemoryCopyStatus.InvalidLength, true);
        }

        uint gdtPhysicalAddress = MemoryUtils.ToPhysicalAddress(gdtSegment, gdtOffset);
        var gdt = new GlobalDescriptorTable(Memory, gdtPhysicalAddress);

        uint sourceAddress = gdt.LinearSourceAddress;
        uint destinationAddress = gdt.LinearDestAddress;

        // Validate addresses for overflow
        if (sourceAddress + byteCount < sourceAddress) {
            _a20Gate.IsEnabled = prevA20Enable;
            return ((byte)ExtendedMemoryCopyStatus.InvalidSource, true);
        }

        if (destinationAddress + byteCount < destinationAddress) {
            _a20Gate.IsEnabled = prevA20Enable;
            return ((byte)ExtendedMemoryCopyStatus.InvalidDestination, true);
        }

        // Validate memory bounds - ensure we don't exceed available memory
        uint maxMemoryAddress = (uint)Memory.Length;
        if (sourceAddress + byteCount > maxMemoryAddress) {
            _a20Gate.IsEnabled = prevA20Enable;
            return ((byte)ExtendedMemoryCopyStatus.InvalidSource, true);
        }

        if (destinationAddress + byteCount > maxMemoryAddress) {
            _a20Gate.IsEnabled = prevA20Enable;
            return ((byte)ExtendedMemoryCopyStatus.InvalidDestination, true);
        }

        // Check for problematic overlap where source == destination would be a no-op anyway
        if (sourceAddress == destinationAddress) {
            _a20Gate.IsEnabled = prevA20Enable;
            return ((byte)ExtendedMemoryCopyStatus.SourceCopiedIntoDest, false);
        }

        // Perform the memory copy using spans (following XMS pattern)
        IList<byte> sourceSpan = Memory.GetSlice((int)sourceAddress, (int)byteCount);
        IList<byte> destinationSpan = Memory.GetSlice((int)destinationAddress, (int)byteCount);

        sourceSpan.CopyTo(destinationSpan);

        // Restore A20 state
        _a20Gate.IsEnabled = prevA20Enable;
        return ((byte)ExtendedMemoryCopyStatus.SourceCopiedIntoDest, false);
    }

    /// <summary>
    /// INT 15h, AH=87h - SYSTEM - COPY EXTENDED MEMORY
    /// <para>
    /// Copies data in extended memory using a global descriptor table.
    /// This is a reimplementation of the SeaBIOS handle_1587 function.
    /// </para><br/>
    /// <b>Inputs:</b><br/>
    /// AH = 87h<br/>
    /// CX = number of words to copy (maximum 8000h)<br/>
    /// ES:SI = pointer to global descriptor table (see RBIL #00499)<br/>
    /// <b>Outputs:</b><br/>
    /// CF set on error<br/>
    /// CF clear if successful<br/>
    /// AH = status (see RBIL #00498)<br/>
    /// </summary>
    [InterruptService(0x87, Reads = CpuValues.Ah | CpuValues.Cx | CpuValues.Es | CpuValues.Si, Writes = CpuValues.Ah | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite, ExecutionPath = ServiceExecutionPath.Direct)]
    public void CopyExtendedMemory(bool calledFromVm) {
        (State.AH, bool carryFlag) = CopyExtendedMemory(State.ES, State.SI, State.CX);
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>Reports the function as unsupported (AH=86h, CF set).</summary>
    public (byte ah, bool carryFlag) Unsupported() {
        return (0x86, true);
    }

    /// <summary>
    /// This function tells to the emulated program that we are an IBM PC AT, not a IBM PS/2.
    /// </summary>
    [InterruptService(0x06, Name = "Unsupported06", Reads = CpuValues.Ah, Writes = CpuValues.Ah | CpuValues.Carry, ExecutionPath = ServiceExecutionPath.Direct)]
    [InterruptService(0x41, Name = "Unsupported41", Reads = CpuValues.Ah, Writes = CpuValues.Ah | CpuValues.Carry, ExecutionPath = ServiceExecutionPath.Direct)]
    [InterruptService(0xC0, Name = "UnsupportedC0", Reads = CpuValues.Ah, Writes = CpuValues.Ah | CpuValues.Carry, ExecutionPath = ServiceExecutionPath.Direct)]
    [InterruptService(0xC2, Name = "UnsupportedC2", Reads = CpuValues.Ah, Writes = CpuValues.Ah | CpuValues.Carry, ExecutionPath = ServiceExecutionPath.Direct)]
    [InterruptService(0xC4, Name = "UnsupportedC4", Reads = CpuValues.Ah, Writes = CpuValues.Ah | CpuValues.Carry, ExecutionPath = ServiceExecutionPath.Direct)]
    public void WriteUnsupported() {
        (State.AH, bool carryFlag) = Unsupported();
        SetCarryFlag(carryFlag, true);
    }

    private void ModifyCmosRegister(byte register, Func<byte, byte> modifier) {
        _ioPortDispatcher.WriteByte(Devices.Cmos.CmosPorts.Address, register);
        byte currentValue = _ioPortDispatcher.ReadByte(Devices.Cmos.CmosPorts.Data);
        byte newValue = modifier(currentValue);
        _ioPortDispatcher.WriteByte(Devices.Cmos.CmosPorts.Address, register);
        _ioPortDispatcher.WriteByte(Devices.Cmos.CmosPorts.Data, newValue);
    }

    /// <summary>Schedules a BIOS wait of <paramref name="microseconds"/>; AH is
    /// preserved, or 0x83 when the timer is already in use.</summary>
    public (byte? ah, bool carryFlag) BiosWait(uint microseconds) {
        if (_biosDataArea.RtcWaitFlag != 0) {
            return (0x83, true);
        }

        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("BIOS WAIT requested for {Microseconds} microseconds", microseconds);
        }

        double delayMs = (microseconds / 1000.0) + 1.0;
        _biosDataArea.RtcWaitFlag = 1;
        _biosDataArea.UserWaitTimeout = microseconds;
        _emulationLoopScheduler.AddEvent(OnWaitComplete, delayMs);
        return (null, false);
    }

    /// <summary>
    /// INT 15h, AH=86h - BIOS Wait.
    /// Waits for CX:DX microseconds. Returns CF=1 + AH=83h if timer already in use.
    /// </summary>
    [InterruptService(0x86, Reads = CpuValues.Ah | CpuValues.Cx | CpuValues.Dx, Writes = CpuValues.Ah | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite, ExecutionPath = ServiceExecutionPath.Direct)]
    public void BiosWait(bool calledFromVm) {
        (byte? ah, bool carryFlag) = BiosWait(((uint)State.CX << 16) | State.DX);
        if (ah is { } status) {
            State.AH = status;
        }
        SetCarryFlag(carryFlag, calledFromVm);
    }

    private void OnWaitComplete(uint value) {
        _biosDataArea.RtcWaitFlag = 0;
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("BIOS WAIT completed");
        }
    }

    /// <summary>Keyboard intercept: leaves <paramref name="scanCode"/> to be
    /// processed (CF set).</summary>
    public bool KeyboardIntercept(byte scanCode) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("INT 15h AH=4Fh: Keyboard intercept called with scan code {ScanCode:X2}", scanCode);
        }

        return true;
    }

    /// <summary>
    /// INT 15h, AH=4Fh - Keyboard Intercept.
    /// Allows translation or filtering of keyboard scan codes. Returns CF=1 to process, CF=0 to ignore.
    /// </summary>
    [InterruptService(0x4F, Reads = CpuValues.Ah | CpuValues.Al, Writes = CpuValues.Carry, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void KeyboardIntercept(bool calledFromVm) {
        bool carryFlag = KeyboardIntercept(State.AL);
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>Device busy notification; reports no error.</summary>
    public (byte ah, bool carryFlag) DeviceBusy() {
        return (0, false);
    }

    /// <summary>
    /// INT 15h, AH=90h - Device Busy.
    /// </summary>
    [InterruptService(0x90, Reads = CpuValues.Ah, Writes = CpuValues.Ah | CpuValues.Carry, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void DeviceBusy(bool calledFromVm) {
        (State.AH, bool carryFlag) = DeviceBusy();
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>Device post-complete notification; reports no error.</summary>
    public (byte ah, bool carryFlag) DevicePost() {
        return (0, false);
    }

    /// <summary>
    /// INT 15h, AH=91h - Device Post.
    /// </summary>
    [InterruptService(0x91, Reads = CpuValues.Ah, Writes = CpuValues.Ah | CpuValues.Carry, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void DevicePost(bool calledFromVm) {
        (State.AH, bool carryFlag) = DevicePost();
        SetCarryFlag(carryFlag, calledFromVm);
    }
}
