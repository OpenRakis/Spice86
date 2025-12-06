namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Enums;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.Memory;
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
    private readonly DualPic _dualPic;
    private readonly IOPorts.IOPortDispatcher _ioPortDispatcher;

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
    /// <param name="dualPic">The PIC for timing operations.</param>
    /// <param name="ioPortDispatcher">The I/O port dispatcher for accessing hardware ports (e.g., CMOS).</param>
    /// <param name="initializeResetVector">Whether to initialize the reset vector with a HLT instruction.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public SystemBiosInt15Handler(Configuration configuration, IMemory memory,
        IFunctionHandlerProvider functionHandlerProvider, Stack stack,
        State state, A20Gate a20Gate, BiosDataArea biosDataArea, DualPic dualPic,
        IOPorts.IOPortDispatcher ioPortDispatcher, bool initializeResetVector,
        ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _a20Gate = a20Gate;
        _configuration = configuration;
        _biosDataArea = biosDataArea;
        _dualPic = dualPic;
        _ioPortDispatcher = ioPortDispatcher;
        if (initializeResetVector) {
            memory.UInt16[0xF000, 0xFFF0] = 0xF4;
        }
        FillDispatchTable();
    }

    private void FillDispatchTable() {
        AddAction(0x24, () => ToggleA20GateOrGetStatus(true));
        AddAction(0x6, Unsupported);
        AddAction(0x86, () => BiosWait(true));
        AddAction(0x90, () => DeviceBusy(true));
        AddAction(0x91, () => DevicePost(true));
        AddAction(0xC0, Unsupported);
        AddAction(0xC2, Unsupported);
        AddAction(0xC4, Unsupported);
        AddAction(0x88, () => GetExtendedMemorySize(true));
        AddAction(0x87, () => CopyExtendedMemory(true));
        AddAction(0x83, () => WaitFunction(true));
        AddAction(0x4F, () => KeyboardIntercept(true));
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x15;

    /// <inheritdoc />
    public override void Run() {
        byte operation = State.AH;
        Run(operation);
    }

    /// <summary>
    /// INT 15h, AH=83h - Event Wait Interval.
    /// Sets up RTC periodic interrupt for timed delay. ES:BX points to a flag byte whose bit 7 will be set when the wait completes.
    /// AL=00h to set, AL=01h to cancel. Returns CF=1 + AH=80h if event already in progress.
    /// </summary>
    public void WaitFunction(bool calledFromVm) {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 15h, AH=83h - WAIT FUNCTION, AL={AL:X2}", State.AL);
        }

        if (State.AL == 0x01) {
            _biosDataArea.RtcWaitFlag = 0;
            ModifyCmosRegister(Devices.Cmos.CmosRegisterAddresses.StatusRegisterB, value => (byte)(value & ~0x40));

            SetCarryFlag(false, calledFromVm);

            if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
                LoggerService.Verbose("WAIT FUNCTION cancelled");
            }
            return;
        }

        if (_biosDataArea.RtcWaitFlag != 0) {
            State.AH = 0x80;
            SetCarryFlag(true, calledFromVm);

            if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
                LoggerService.Warning("WAIT FUNCTION called while event already in progress");
            }
            return;
        }

        uint count = ((uint)State.CX << 16) | State.DX;
        _biosDataArea.UserWaitCompleteFlag = new SegmentedAddress(State.ES, State.BX);
        _biosDataArea.UserWaitTimeout = count;
        _biosDataArea.RtcWaitFlag = 1;
        ModifyCmosRegister(Devices.Cmos.CmosRegisterAddresses.StatusRegisterB, value => (byte)(value | 0x40));

        SetCarryFlag(false, calledFromVm);

        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("WAIT FUNCTION set: count={Count} microseconds, callback={Segment:X4}:{Offset:X4}",
                count, State.ES, State.BX);
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
    public void ToggleA20GateOrGetStatus(bool calledFromVm) {
        switch (State.AL) {
            case 0:
                _a20Gate.IsEnabled = false;
                SetCarryFlag(false, calledFromVm);
                break;
            case 1:
                _a20Gate.IsEnabled = true;
                SetCarryFlag(false, calledFromVm);
                break;
            case 2:
                State.AL = (byte)(_a20Gate.IsEnabled ? 0x1 : 0x0);
                State.AH = 0; // success
                SetCarryFlag(false, calledFromVm);
                break;
            case 3:
                _a20Gate.IsEnabled = false;
                State.BX = 0x3; //Bitmask, keyboard and 0x92;
                State.AH = 0; // success
                SetCarryFlag(false, calledFromVm);
                break;

            default:
                if (LoggerService.IsEnabled(LogEventLevel.Error)) {
                    LoggerService.Error("Unrecognized command in AL for {MethodName}",
                        nameof(ToggleA20GateOrGetStatus));
                }
                break;
        }
    }

    /// <summary>
    /// INT 15h, AH=88h - Get Extended Memory Size.
    /// Returns contiguous KB starting at 0x100000. Standard BIOS returns memory between 1MB and 16MB.
    /// </summary>
    public void GetExtendedMemorySize(bool calledFromVm) {
        if (_a20Gate.IsEnabled || _configuration.Xms is true) {
            State.AX = 0; //Either the HMA is not accessible, or the DOS driver protects it.
        } else {
            State.AX = (ushort)(A20Gate.EndOfHighMemoryArea - A20Gate.StartOfHighMemoryArea);
        }
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// INT 15h, AH=87h - Copy Extended Memory.
    /// Copies data using global descriptor table. CX=words to copy (max 8000h), ES:SI=GDT pointer.
    /// </summary>
    public void CopyExtendedMemory(bool calledFromVm) {
        bool prevA20Enable = _a20Gate.IsEnabled;
        _a20Gate.IsEnabled = true;

        uint wordCount = State.CX;
        uint byteCount = wordCount * 2;

        if (wordCount == 0) {
            SetCarryFlag(false, calledFromVm);
            State.AH = (byte)ExtendedMemoryCopyStatus.SourceCopiedIntoDest;
            _a20Gate.IsEnabled = prevA20Enable;
            return;
        }

        if (wordCount > 0x8000) {
            SetCarryFlag(true, calledFromVm);
            State.AH = (byte)ExtendedMemoryCopyStatus.InvalidLength;
            _a20Gate.IsEnabled = prevA20Enable;
            return;
        }

        uint gdtPhysicalAddress = MemoryUtils.ToPhysicalAddress(State.ES, State.SI);
        var gdt = new GlobalDescriptorTable(Memory, gdtPhysicalAddress);

        uint sourceAddress = gdt.LinearSourceAddress;
        uint destinationAddress = gdt.LinearDestAddress;

        if (sourceAddress + byteCount < sourceAddress) {
            SetCarryFlag(true, calledFromVm);
            State.AH = (byte)ExtendedMemoryCopyStatus.InvalidSource;
            _a20Gate.IsEnabled = prevA20Enable;
            return;
        }

        if (destinationAddress + byteCount < destinationAddress) {
            SetCarryFlag(true, calledFromVm);
            State.AH = (byte)ExtendedMemoryCopyStatus.InvalidDestination;
            _a20Gate.IsEnabled = prevA20Enable;
            return;
        }

        uint maxMemoryAddress = (uint)Memory.Length;
        if (sourceAddress + byteCount > maxMemoryAddress) {
            SetCarryFlag(true, calledFromVm);
            State.AH = (byte)ExtendedMemoryCopyStatus.InvalidSource;
            _a20Gate.IsEnabled = prevA20Enable;
            return;
        }

        if (destinationAddress + byteCount > maxMemoryAddress) {
            SetCarryFlag(true, calledFromVm);
            State.AH = (byte)ExtendedMemoryCopyStatus.InvalidDestination;
            _a20Gate.IsEnabled = prevA20Enable;
            return;
        }

        if (sourceAddress == destinationAddress) {
            SetCarryFlag(false, calledFromVm);
            State.AH = (byte)ExtendedMemoryCopyStatus.SourceCopiedIntoDest;
            _a20Gate.IsEnabled = prevA20Enable;
            return;
        }

        IList<byte> sourceSpan = Memory.GetSlice((int)sourceAddress, (int)byteCount);
        IList<byte> destinationSpan = Memory.GetSlice((int)destinationAddress, (int)byteCount);

        sourceSpan.CopyTo(destinationSpan);

        _a20Gate.IsEnabled = prevA20Enable;
        SetCarryFlag(false, calledFromVm);
        State.AH = (byte)ExtendedMemoryCopyStatus.SourceCopiedIntoDest;
    }

    /// <summary>
    /// This function tells to the emulated program that we are an IBM PC AT, not a IBM PS/2.
    /// </summary>
    public void Unsupported() {
        SetCarryFlag(true, true);
        State.AH = 0x86;
    }


    private void ModifyCmosRegister(byte register, Func<byte, byte> modifier) {
        _ioPortDispatcher.WriteByte(Devices.Cmos.CmosPorts.Address, register);
        byte currentValue = _ioPortDispatcher.ReadByte(Devices.Cmos.CmosPorts.Data);
        byte newValue = modifier(currentValue);
        _ioPortDispatcher.WriteByte(Devices.Cmos.CmosPorts.Address, register);
        _ioPortDispatcher.WriteByte(Devices.Cmos.CmosPorts.Data, newValue);
    }

    /// <summary>
    /// INT 15h, AH=86h - BIOS Wait.
    /// Waits for CX:DX microseconds. Returns CF=1 + AH=83h if timer already in use.
    /// </summary>
    public void BiosWait(bool calledFromVm) {
        if (_biosDataArea.RtcWaitFlag != 0) {
            State.AH = 0x83;
            SetCarryFlag(true, calledFromVm);
            return;
        }

        uint microseconds = ((uint)State.CX << 16) | State.DX;

        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("BIOS WAIT requested for {Microseconds} microseconds", microseconds);
        }

        double delayMs = (microseconds / 1000.0) + 1.0;
        _biosDataArea.RtcWaitFlag = 1;
        _biosDataArea.UserWaitTimeout = microseconds;
        _dualPic.AddEvent(OnWaitComplete, delayMs);
        SetCarryFlag(false, calledFromVm);
    }


    private void OnWaitComplete(uint value) {
        _biosDataArea.RtcWaitFlag = 0;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("BIOS WAIT completed");
        }
    }

    /// <summary>
    /// INT 15h, AH=4Fh - Keyboard Intercept.
    /// Allows translation or filtering of keyboard scan codes. Returns CF=1 to process, CF=0 to ignore.
    /// </summary>
    public void KeyboardIntercept(bool calledFromVm) {
        byte scanCode = State.AL;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 15h AH=4Fh: Keyboard intercept called with scan code {ScanCode:X2}", scanCode);
        }

        SetCarryFlag(true, calledFromVm);
    }

    /// <summary>
    /// INT 15h, AH=90h - Device Busy.
    /// </summary>
    public void DeviceBusy(bool calledFromVm) {
        SetCarryFlag(false, calledFromVm);
        State.AH = 0;
    }

    /// <summary>
    /// INT 15h, AH=91h - Device Post.
    /// </summary>
    public void DevicePost(bool calledFromVm) {
        SetCarryFlag(false, calledFromVm);
        State.AH = 0;
    }
}