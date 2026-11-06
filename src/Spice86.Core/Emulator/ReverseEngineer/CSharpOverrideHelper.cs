namespace Spice86.Core.Emulator.ReverseEngineer;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Devices.Cmos;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Joystick;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.Devices.Sound.Midi;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.ReverseEngineer.InterruptServices;
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Utils;

using System.Collections.Immutable;
using System.Linq;
using System.Numerics;

/// <summary>
/// Provides a set of properties and methods to facilitate the creation of C# overrides of machine code.
/// </summary>
public class CSharpOverrideHelper {
    /// <summary>
    /// The two programmable interrupt controllers.
    /// </summary>
    protected readonly DualPic _dualPic;

    /// <summary>
    /// The class that stores callback instructions definitions.
    /// </summary>
    protected readonly CallbackHandler _callbackHandler;

    /// <summary>
    /// The IBM PC timer device.
    /// </summary>
    protected readonly PitTimer _timer;

    /// <summary>
    /// The class that manages software breakpoints.
    /// </summary>
    protected readonly EmulatorBreakpointsManager EmulatorBreakpointsManager;

    /// <summary>
    /// The service used for logging.
    /// </summary>
    protected readonly ILogger _loggerService;

    /// <summary>
    /// The Spice86 configuration
    /// </summary>
    protected Configuration Configuration { get; }

    /// <summary>
    /// The emulator machine.
    /// </summary>
    public Machine Machine { get; }

    private readonly InterruptVectorTable _interruptVectorTable;

    private readonly HashSet<(SegmentedAddress, SegmentedAddress)> _warnedNestedOverrideCalls = new();

    /// <summary>
    /// The memory bus of the IBM PC.
    /// </summary>
    public IMemory Memory { get; }

    /// <summary>
    /// Gets the 8-bit indexer of the memory bus.
    /// </summary>
    public UInt8Indexer UInt8 => Memory.UInt8;

    /// <summary>
    /// Gets the 16-bit indexer of the memory bus.
    /// </summary>
    public UInt16Indexer UInt16 => Memory.UInt16;

    /// <summary>
    /// Gets the 32-bit indexer of the memory bus.
    /// </summary>
    public UInt32Indexer UInt32 => Memory.UInt32;

    /// <summary>
    /// Gets the big-endian 16-bit indexer of the memory bus.
    /// </summary>
    public UInt16BigEndianIndexer UInt16BigEndian => Memory.UInt16BigEndian;

    /// <summary>
    /// Gets the signed 8-bit indexer of the memory bus.
    /// </summary>
    public Int8Indexer Int8 => Memory.Int8;

    /// <summary>
    /// Gets the signed 16-bit indexer of the memory bus.
    /// </summary>
    public Int16Indexer Int16 => Memory.Int16;

    /// <summary>
    /// Gets the signed 32-bit indexer of the memory bus.
    /// </summary>
    public Int32Indexer Int32 => Memory.Int32;

    /// <summary>
    /// Gets the SS-relative 8-bit indexer of the memory bus: stack accesses, so a segment-limit
    /// fault raises #SS like the CPU's own SS-relative access.
    /// </summary>
    public SsIndexer<byte> SsUInt8 { get; }

    /// <summary>
    /// Gets the SS-relative 16-bit indexer of the memory bus, a stack access.
    /// </summary>
    public SsIndexer<ushort> SsUInt16 { get; }

    /// <summary>
    /// Gets the SS-relative 32-bit indexer of the memory bus, a stack access.
    /// </summary>
    public SsIndexer<uint> SsUInt32 { get; }

    /// <summary>
    /// Gets the SS-relative signed 8-bit indexer of the memory bus, a stack access.
    /// </summary>
    public SsIndexer<sbyte> SsInt8 { get; }

    /// <summary>
    /// Gets the SS-relative signed 16-bit indexer of the memory bus, a stack access.
    /// </summary>
    public SsIndexer<short> SsInt16 { get; }

    /// <summary>
    /// Gets the SS-relative signed 32-bit indexer of the memory bus, a stack access.
    /// </summary>
    public SsIndexer<int> SsInt32 { get; }

    /// <summary>
    /// Gets the 16-bit segmented address indexer of the memory bus.
    /// </summary>
    public SegmentedAddress16Indexer SegmentedAddress16 => Memory.SegmentedAddress16;

    /// <summary>
    /// Gets the 32-bit segmented address indexer of the memory bus.
    /// </summary>
    public SegmentedAddress32Indexer SegmentedAddress32 => Memory.SegmentedAddress32;

    /// <summary>
    /// Gets the stack of the CPU.
    /// </summary>
    public Stack Stack => Machine.Stack;

    /// <summary>
    /// Gets the state of the CPU.
    /// </summary>
    public State State => Machine.CpuState;

    /// <summary>
    /// Arithmetic-logic unit for 8 bit operations
    /// </summary>
    public Alu8 Alu8 { get; }

    /// <summary>
    /// Arithmetic-logic unit for 16 bit operations
    /// </summary>
    public Alu16 Alu16 { get; }

    /// <summary>
    /// Arithmetic-logic unit for 32 bit operations
    /// </summary>
    public Alu32 Alu32 { get; }

    /// <summary>
    /// Gets or sets the value of AX register.
    /// </summary>
    public ushort AX { get => State.AX; set => State.AX = value; }

    /// <summary>
    /// Gets or sets the value of AH register.
    /// </summary>
    public byte AH { get => State.AH; set => State.AH = value; }

    /// <summary>
    /// Gets or sets the value of AL register.
    /// </summary>
    public byte AL { get => State.AL; set => State.AL = value; }

    /// <summary>
    /// Gets or sets the value of BX register.
    /// </summary>
    public ushort BX { get => State.BX; set => State.BX = value; }

    /// <summary>
    /// Gets or sets the value of BH register.
    /// </summary>
    public byte BH { get => State.BH; set => State.BH = value; }

    /// <summary>
    /// Gets or sets the value of BL register.
    /// </summary>
    public byte BL { get => State.BL; set => State.BL = value; }

    /// <summary>
    /// Gets or sets the value of CX register.
    /// </summary>
    public ushort CX { get => State.CX; set => State.CX = value; }

    /// <summary>
    /// Gets or sets the value of CH register.
    /// </summary>
    public byte CH { get => State.CH; set => State.CH = value; }

    /// <summary>
    /// Gets or sets the value of CL register.
    /// </summary>
    public byte CL { get => State.CL; set => State.CL = value; }

    /// <summary>
    /// Gets or sets the value of DX register.
    /// </summary>
    public ushort DX { get => State.DX; set => State.DX = value; }

    /// <summary>
    /// Gets or sets the value of DH register.
    /// </summary>
    public byte DH { get => State.DH; set => State.DH = value; }

    /// <summary>
    /// Gets or sets the value of DL register.
    /// </summary>
    public byte DL { get => State.DL; set => State.DL = value; }

    /// <summary>
    /// Gets or sets the value of SP register.
    /// </summary>
    public ushort SP { get => State.SP; set => State.SP = value; }

    /// <summary>
    /// Gets or sets the value of BP register.
    /// </summary>
    public ushort BP { get => State.BP; set => State.BP = value; }

    /// <summary>
    /// Gets or sets the value of SI register.
    /// </summary>
    public ushort SI { get => State.SI; set => State.SI = value; }

    /// <summary>
    /// Gets or sets the value of DI register.
    /// </summary>
    public ushort DI { get => State.DI; set => State.DI = value; }

    /// <summary>
    /// Gets or sets the value of CS register.
    /// </summary>
    public ushort CS { get => State.CS; set => State.CS = value; }

    /// <summary>
    /// Gets or sets the value of DS register.
    /// </summary>
    public ushort DS { get => State.DS; set => State.DS = value; }

    /// <summary>
    /// Gets or sets the value of ES register.
    /// </summary>
    public ushort ES { get => State.ES; set => State.ES = value; }

    /// <summary>
    /// Gets or sets the value of FS register.
    /// </summary>
    public ushort FS { get => State.FS; set => State.FS = value; }

    /// <summary>
    /// Gets or sets the value of GS register.
    /// </summary>
    public ushort GS { get => State.GS; set => State.GS = value; }

    /// <summary>
    /// Gets or sets the value of SS register.
    /// </summary>
    public ushort SS { get => State.SS; set => State.SS = value; }

    /// <summary>
    /// Gets or sets the value of IP register.
    /// </summary>
    public ushort IP { get => State.IP; set => State.IP = value; }

    /// <summary>
    /// Gets or sets the value of the 32-bit EAX register.
    /// </summary>
    public uint EAX { get => State.EAX; set => State.EAX = value; }

    /// <summary>
    /// Gets or sets the value of the 32-bit EBX register.
    /// </summary>
    public uint EBX { get => State.EBX; set => State.EBX = value; }

    /// <summary>
    /// Gets or sets the value of the 32-bit ECX register.
    /// </summary>
    public uint ECX { get => State.ECX; set => State.ECX = value; }

    /// <summary>
    /// Gets or sets the value of the 32-bit EDX register.
    /// </summary>
    public uint EDX { get => State.EDX; set => State.EDX = value; }

    /// <summary>
    /// Gets or sets the value of the 32-bit ESP register.
    /// </summary>
    public uint ESP { get => State.ESP; set => State.ESP = value; }

    /// <summary>
    /// Gets or sets the value of the 32-bit EBP register.
    /// </summary>
    public uint EBP { get => State.EBP; set => State.EBP = value; }

    /// <summary>
    /// Gets or sets the value of the 32-bit ESI register.
    /// </summary>
    public uint ESI { get => State.ESI; set => State.ESI = value; }

    /// <summary>
    /// Gets or sets the value of the 32-bit EDI register.
    /// </summary>
    public uint EDI { get => State.EDI; set => State.EDI = value; }

    /// <summary>
    /// Gets or sets the value of the auxiliary flag.
    /// </summary>
    public bool AuxiliaryFlag { get => State.AuxiliaryFlag; set => State.AuxiliaryFlag = value; }

    /// <summary>
    /// Gets or sets the value of the carry flag.
    /// </summary>
    public bool CarryFlag { get => State.CarryFlag; set => State.CarryFlag = value; }

    /// <summary>
    /// Gets or sets the value of the direction flag.
    /// </summary>
    public bool DirectionFlag { get => State.DirectionFlag; set => State.DirectionFlag = value; }

    /// <summary>
    /// Gets or sets the value of the interrupt flag.
    /// </summary>
    public bool InterruptFlag { get => State.InterruptFlag; set => State.InterruptFlag = value; }

    /// <summary>
    /// Gets or sets the value of the overflow flag.
    /// </summary>
    public bool OverflowFlag { get => State.OverflowFlag; set => State.OverflowFlag = value; }

    /// <summary>
    /// Gets or sets the value of the parity flag.
    /// </summary>
    public bool ParityFlag { get => State.ParityFlag; set => State.ParityFlag = value; }

    /// <summary>
    /// Gets or sets the value of the sign flag.
    /// </summary>
    public bool SignFlag { get => State.SignFlag; set => State.SignFlag = value; }

    /// <summary>
    /// Gets or sets the value of the trap flag.
    /// </summary>
    public bool TrapFlag { get => State.TrapFlag; set => State.TrapFlag = value; }

    /// <summary>
    /// Gets or sets the value of the zero flag.
    /// </summary>
    public bool ZeroFlag { get => State.ZeroFlag; set => State.ZeroFlag = value; }

    /// <summary>
    /// Gets or sets the value of the flags register (32 bit value).
    /// </summary>
    public uint FlagRegister { get => State.Flags.FlagRegister; set => State.Flags.FlagRegister = value; }

    /// <summary>
    /// Gets or sets the value of the flags register (16 bit value).
    /// </summary>
    public ushort FlagRegister16 { get => State.Flags.FlagRegister16; set => State.Flags.FlagRegister = value; }

    /// <summary>
    /// Gets the offset value of the Direction Flag for 8 bit CPU instructions.
    /// </summary>
    public short Direction8 => State.Direction8;

    /// <summary>
    /// Gets the offset value of the Direction Flag for 16 bit CPU instructions.
    /// </summary>
    public short Direction16 => State.Direction16;

    /// <summary>
    /// Gets the offset value of the Direction Flag for 32 bit CPU instructions.
    /// </summary>
    public short Direction32 => State.Direction32;

    /// <summary>
    /// A dictionary that stores the function information of each function defined in memory.
    /// </summary>
    public IDictionary<SegmentedAddress, FunctionInformation> FunctionInformations { get; private set; }

    /// <summary>
    /// Gets or sets the <see cref="JumpDispatcher"/>
    /// </summary>
    public JumpDispatcher JumpDispatcher { get; set; }

    public ReturnOperationsHelper ReturnOperationsHelper { get; }

    /// <summary>
    /// Typed wrappers for the VGA I/O ports.
    /// </summary>
    protected VgaPorts VgaPorts { get; }

    /// <summary>
    /// Typed wrappers for the programmable interval timer I/O ports.
    /// </summary>
    protected PitPorts PitPorts { get; }

    /// <summary>
    /// Typed wrappers for the interrupt controller I/O ports.
    /// </summary>
    protected PicPorts PicPorts { get; }

    /// <summary>
    /// Typed wrappers for the DMA controller I/O ports.
    /// </summary>
    protected DmaPorts DmaPorts { get; }

    /// <summary>
    /// Typed wrappers for the keyboard controller I/O ports.
    /// </summary>
    protected KeyboardControllerPorts KeyboardControllerPorts { get; }

    /// <summary>
    /// Typed wrappers for the CMOS/RTC I/O ports.
    /// </summary>
    protected RealTimeClockPorts RealTimeClockPorts { get; }

    /// <summary>
    /// Typed wrappers for the PC speaker PPI port.
    /// </summary>
    protected PcSpeakerPorts PcSpeakerPorts { get; }

    /// <summary>
    /// Typed wrappers for the Sound Blaster I/O ports.
    /// </summary>
    protected SoundBlasterPorts SoundBlasterPorts { get; }

    /// <summary>
    /// Typed wrappers for the OPL I/O ports.
    /// </summary>
    protected Opl3Ports Opl3Ports { get; }

    /// <summary>
    /// Typed wrappers for the MIDI I/O ports.
    /// </summary>
    protected MidiPorts MidiPorts { get; }

    /// <summary>
    /// Typed wrappers for the Gravis Ultrasound I/O ports.
    /// </summary>
    protected GravisUltrasoundPorts GravisUltrasoundPorts { get; }

    /// <summary>
    /// Typed wrappers for the joystick game port.
    /// </summary>
    protected JoystickPorts JoystickPorts { get; }

    /// <summary>
    /// Typed wrappers for the floppy disk controller I/O ports.
    /// </summary>
    protected FloppyControllerPorts FloppyControllerPorts { get; }

    /// <summary>
    /// The CMOS/RTC device; its register operations are the device-level form of the port 0x70/0x71
    /// sequences. The interrupt-service helper of int 70h is <see cref="Rtc"/>.
    /// </summary>
    protected RealTimeClock Cmos { get; }

    /// <summary>
    /// The programmable interval timer device; its channel programming operation is the
    /// device-level form of the port 0x43 / 0x40+channel sequences.
    /// </summary>
    protected PitTimer Pit { get; }

    /// <summary>
    /// The VGA I/O port device; its register and palette operations are the device-level form of the
    /// index/data port sequences.
    /// </summary>
    protected VgaIoPortHandler Vga => Machine.VgaIoPortHandler as VgaIoPortHandler
        ?? throw FailAsUntested("The installed VGA port handler has no device-operation API");

    /// <summary>
    /// The OPL synthesizer device; its register write operation is the device-level form of the
    /// address/data port pairs.
    /// </summary>
    protected Opl3Fm Opl { get; }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="functionInformations">The dictionary of functions information. Each one can define an optional C# code override of the machine code.</param>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="configuration">The emulator configuration.</param>
    public CSharpOverrideHelper(IDictionary<SegmentedAddress, FunctionInformation> functionInformations,
        Machine machine, ILogger loggerService, Configuration configuration) {
        Machine = machine;
        Memory = machine.Memory;
        _dualPic = machine.DualPic;
        _timer = machine.Timer;
        _callbackHandler = machine.CallbackHandler;
        EmulatorBreakpointsManager = machine.EmulatorBreakpointsManager;
        _loggerService = loggerService;
        Configuration = configuration;
        FunctionInformations = functionInformations;
        Machine = machine;
        _interruptVectorTable = machine.InterruptVectorTable;
        JumpDispatcher = new();
        SsUInt8 = new(Memory.UInt8, machine.CpuState.SegmentRegisters);
        SsUInt16 = new(Memory.UInt16, machine.CpuState.SegmentRegisters);
        SsUInt32 = new(Memory.UInt32, machine.CpuState.SegmentRegisters);
        SsInt8 = new(Memory.Int8, machine.CpuState.SegmentRegisters);
        SsInt16 = new(Memory.Int16, machine.CpuState.SegmentRegisters);
        SsInt32 = new(Memory.Int32, machine.CpuState.SegmentRegisters);
        Alu8 = new(machine.CpuState);
        Alu16 = new(machine.CpuState);
        Alu32 = new(machine.CpuState);
        ReturnOperationsHelper = new(State, Stack);
        VgaPorts = new(machine.IoPortDispatcher);
        PitPorts = new(machine.IoPortDispatcher);
        PicPorts = new(machine.IoPortDispatcher);
        DmaPorts = new(machine.IoPortDispatcher);
        KeyboardControllerPorts = new(machine.IoPortDispatcher);
        RealTimeClockPorts = new(machine.IoPortDispatcher);
        PcSpeakerPorts = new(machine.IoPortDispatcher);
        SoundBlasterPorts = new(machine.IoPortDispatcher);
        Opl3Ports = new(machine.IoPortDispatcher);
        MidiPorts = new(machine.IoPortDispatcher);
        GravisUltrasoundPorts = new(machine.IoPortDispatcher);
        JoystickPorts = new(machine.IoPortDispatcher);
        FloppyControllerPorts = new(machine.IoPortDispatcher);
        Cmos = machine.RealTimeClock;
        Pit = machine.Timer;
        Opl = machine.OPL;
        Video = new VideoServices(this);
        Equipment = new EquipmentServices(this);
        MemorySize = new MemorySizeServices(this);
        Disk = new DiskServices(this);
        Serial = new SerialServices(this);
        SystemBios = new SystemBiosServices(this);
        Keyboard = new KeyboardServices(this);
        Printer = new PrinterServices(this);
        Clock = new ClockServices(this);
        UserTimer = new UserTimerServices(this);
        Dos = new DosServices(this);
        CriticalError = new CriticalErrorServices(this);
        DosIdle = new DosIdleServices(this);
        DosNetwork = new DosNetworkServices(this);
        DosMultiplex = new DosMultiplexServices(this);
        Mouse = new MouseServices(this);
        Ems = new EmsServices(this);
        Rtc = new RtcServices(this);
    }

    /// <summary>Typed helpers for the INT 10h video services.</summary>
    public VideoServices Video { get; }

    /// <summary>Typed helpers for the INT 11h equipment list service.</summary>
    public EquipmentServices Equipment { get; }

    /// <summary>Typed helpers for the INT 12h conventional memory size service.</summary>
    public MemorySizeServices MemorySize { get; }

    /// <summary>Typed helpers for the INT 13h disk services.</summary>
    public DiskServices Disk { get; }

    /// <summary>Typed helpers for the INT 14h serial port services.</summary>
    public SerialServices Serial { get; }

    /// <summary>Typed helpers for the INT 15h system BIOS services.</summary>
    public SystemBiosServices SystemBios { get; }

    /// <summary>Typed helpers for the INT 16h keyboard services.</summary>
    public KeyboardServices Keyboard { get; }

    /// <summary>Typed helpers for the INT 17h printer services.</summary>
    public PrinterServices Printer { get; }

    /// <summary>Typed helpers for the INT 1Ah clock services.</summary>
    public ClockServices Clock { get; }

    /// <summary>Typed helpers for the INT 1Ch user timer tick.</summary>
    public UserTimerServices UserTimer { get; }

    /// <summary>Typed helpers for the INT 21h DOS services.</summary>
    public DosServices Dos { get; }

    /// <summary>Typed helpers for the INT 24h critical error handler.</summary>
    public CriticalErrorServices CriticalError { get; }

    /// <summary>Typed helpers for the INT 28h DOS idle service.</summary>
    public DosIdleServices DosIdle { get; }

    /// <summary>Typed helpers for the INT 2Ah DOS network services.</summary>
    public DosNetworkServices DosNetwork { get; }

    /// <summary>Typed helpers for the INT 2Fh DOS multiplex services.</summary>
    public DosMultiplexServices DosMultiplex { get; }

    /// <summary>Typed helpers for the INT 33h mouse services.</summary>
    public MouseServices Mouse { get; }

    /// <summary>Typed helpers for the INT 67h expanded memory services.</summary>
    public EmsServices Ems { get; }

    /// <summary>Typed helpers for the INT 70h real-time clock interrupt.</summary>
    public RtcServices Rtc { get; }

    /// <summary>
    /// Registers a function at the specified segmented address. <br/>
    /// </summary>
    /// <param name="segment">The segment part of the segmented address.</param>
    /// <param name="offset">The offset part of the segmented address.</param>
    /// <param name="name">The name of the function.</param>
    /// <remarks>
    /// Example of a valid function name: 'IncDialogueCount47A8_0x1ED_0xA1E8_0xC0B8'
    /// </remarks>
    public void DefineFunction(ushort segment, ushort offset, string name) {
        SegmentedAddress address = new(segment, offset);
        GetFunctionAtAddress(true, address);
        FunctionInformation functionInformation = new(address, name, null);
        FunctionInformations.Add(address, functionInformation);
    }

    /// <summary>
    /// Registers a function at the specified segmented address. <br/>
    /// </summary>
    /// <param name="segment">The segment part of the segmented address.</param>
    /// <param name="offset">The offset part of the segmented address.</param>
    /// <param name="overrideFunc">The function to register.</param>
    /// <param name="failOnExisting">Whether to fail if a function is already defined at the specified address. Default is true.</param>
    /// <param name="name">The name of the function. If null, the name of the provided function will be parsed using the GhidraSymbolsDumper utility.</param>
    /// <exception cref="UnrecoverableException">Thrown when <paramref name="name"/> is null and the name of the provided function cannot be parsed.</exception>
    /// <remarks>
    /// Example of a valid function name: 'IncDialogueCount47A8_0x1ED_0xA1E8_0xC0B8'
    /// </remarks>
    public void DefineFunction(ushort segment,
        ushort offset,
        Func<int, Action> overrideFunc,
        bool failOnExisting = true,
        string? name = null) {
        RegisterOverride(segment, offset, overrideFunc, failOnExisting, false, name);
    }

    /// <summary>
    /// Registers generated code at the specified segmented address. Generated code owns the complete
    /// control flow of its partitions, so the registration makes every control transfer of the override
    /// land on another override or on the call's expected return address instead of falling back to the
    /// interpreter. Fails when a function is already registered at that address.
    /// </summary>
    /// <param name="segment">The segment part of the segmented address.</param>
    /// <param name="offset">The offset part of the segmented address.</param>
    /// <param name="overrideFunc">The function to register.</param>
    /// <param name="name">The name of the function.</param>
    /// <exception cref="UnrecoverableException">A function is already registered at that address.</exception>
    public void DefineFunctionWithoutInterpreterFallback(ushort segment, ushort offset, Func<int, Action> overrideFunc, string name) {
        RegisterOverride(segment, offset, overrideFunc, true, true, name);
    }

    /// <summary>
    /// Registers an override at the specified segmented address, unless an entry already exists there and
    /// <paramref name="failOnExisting"/> says that must fail. The name is <paramref name="name"/>, or is
    /// parsed from the delegate's method name when it is null.
    /// </summary>
    /// <param name="segment">The segment part of the segmented address.</param>
    /// <param name="offset">The offset part of the segmented address.</param>
    /// <param name="overrideFunc">The function to register.</param>
    /// <param name="failOnExisting">Whether to fail when a function already exists at the specified address.</param>
    /// <param name="forbidsInterpreterFallback">Whether the override owns the complete control flow of its code.</param>
    /// <param name="name">The name of the function, or null to parse it from the delegate's method name.</param>
    /// <exception cref="UnrecoverableException">A function already exists at the address and
    /// <paramref name="failOnExisting"/> is set, or the name cannot be parsed.</exception>
    private void RegisterOverride(ushort segment, ushort offset, Func<int, Action> overrideFunc,
        bool failOnExisting, bool forbidsInterpreterFallback, string? name) {
        SegmentedAddress address = new(segment, offset);
        FunctionInformation? existing = GetFunctionAtAddress(failOnExisting, address);
        if (existing?.HasOverride is true) {
            // Do not overwrite existing code with override
            return;
        }

        string functionName;
        if (name != null) {
            functionName = name;
        } else {
            string methodName = overrideFunc.Method.Name;
            FunctionInformation? parsedFunctionInformation = GhidraSymbolsExporter.NameToFunctionInformation(_loggerService, methodName);
            if (parsedFunctionInformation == null) {
                throw new UnrecoverableException("Cannot parse " + methodName +
                    " into a spice86 function name as format is not correct.");
            }

            functionName = parsedFunctionInformation.Name;
        }

        FunctionInformations[address] = new FunctionInformation(address, functionName, overrideFunc) {
            ForbidsInterpreterFallback = forbidsInterpreterFallback
        };
    }

    /// <summary>
    /// Gets the function information for the function at the specified address.
    /// </summary>
    /// <param name="failOnExisting">A flag indicating whether to throw an exception if a function already exists at the specified address.</param>
    /// <param name="address">The address of the function to retrieve.</param>
    /// <returns>The function information for the function at the specified address, or null if no function exists at that address.</returns>
    /// <exception cref="UnrecoverableException">Thrown if a function already exists at the specified address and failOnExisting is true.</exception>
    public FunctionInformation? GetFunctionAtAddress(bool failOnExisting, SegmentedAddress address) {
        if (FunctionInformations.TryGetValue(address, out FunctionInformation? existingFunctionInformation)) {
            if (!failOnExisting) {
                return existingFunctionInformation;
            }

            string error =
                $"There is already a function overriden at address {address} named {existingFunctionInformation.Name}. Please check your mappings for duplicates.";
            if (_loggerService.IsEnabled(LogLevel.Error)) {
                _loggerService.LogError(
                    "There is already a function defined at address {Address} named {ExistingFunctionInformationName} but you are trying to redefine it. Please check your mappings for duplicates",
                    address, existingFunctionInformation.Name);
            }

            throw new UnrecoverableException(error);
        }

        return null;
    }

    /// <summary>
    /// Returns an <see cref="Action"/> that sets CS and IP to the provided values.
    /// </summary>
    /// <param name="cs">The value for the CS register.</param>
    /// <param name="ip">The value for the IP register.</param>
    /// <returns>The <see cref="Action"/> that will mutate CS and IP when invoked.</returns>
    public Action FarJump(ushort cs, ushort ip) {
        return () => {
            State.CS = cs;
            State.IP = ip;
        };
    }

    /// <summary>
    /// Returns an <see cref="Action"/> that performs a far return instruction when invoked.
    /// </summary>
    /// <returns>returns an <see cref="Action"/> that performs a far return instruction when invoked.</returns>
    public Action FarRet(ushort numberOfBytesToPop = 0) {
        return () => ReturnOperationsHelper.FarRet16(numberOfBytesToPop);
    }

    public Action FarRet32(ushort numberOfBytesToPop = 0) {
        return () => ReturnOperationsHelper.FarRet32(numberOfBytesToPop);
    }

    /// <summary>
    /// Returns an <see cref="Action"/> that performs an IRET instruction when invoked.
    /// </summary>
    /// <returns>returns an <see cref="Action"/> that performs an interrupt return when invoked.</returns>
    public Action InterruptRet() {
        return () => ReturnOperationsHelper.InterruptRet();
    }

    /// <summary>
    /// Returns an <see cref="Action"/> that performs an IRETD instruction (32-bit interrupt return) when invoked.
    /// </summary>
    /// <returns>returns an <see cref="Action"/> that performs a 32-bit interrupt return when invoked.</returns>
    public Action InterruptRet32() {
        return () => ReturnOperationsHelper.InterruptRet32();
    }

    /// <summary>
    /// Returns an <see cref="Action"/> that will modify the IP register to the provided value.
    /// </summary>
    /// <param name="ip">The target value for the IP register.</param>
    /// <returns>The action that will modify the IP register when invoked.</returns>
    public Action NearJump(ushort ip) {
        return () => State.IP = ip;
    }

    /// <summary>
    /// Returns an action that performs a near return.
    /// </summary>
    /// <param name="numberOfBytesToPop">The number of bytes to remove from the stack.</param>
    /// <returns>An action that performs a near return.</returns>
    public Action NearRet(ushort numberOfBytesToPop = 0) {
        return () => ReturnOperationsHelper.NearRet16(numberOfBytesToPop);
    }

    public Action NearRet32(ushort numberOfBytesToPop = 0) {
        return () => ReturnOperationsHelper.NearRet32(numberOfBytesToPop);
    }

    /// <summary>
    /// Performs a near call.
    /// </summary>
    /// <param name="expectedReturnCs">The expected value of the CS register after the call.</param>
    /// <param name="expectedReturnIp">The expected value of the IP register after the call.</param>
    /// <param name="function">The function to call.</param>
    public void NearCall(ushort expectedReturnCs, ushort expectedReturnIp, Func<int, Action> function) {
        NearCallReturnedElsewhere(expectedReturnCs, expectedReturnIp, function, NoRedirectedReturns);
    }

    /// <summary>
    /// Performs a near call and tells whether it returned to one of the addresses the call was observed
    /// to be redirected to instead of <paramref name="expectedReturnCs"/>:<paramref name="expectedReturnIp"/>.
    /// The return address pushed on the stack is still the expected one; a callee that rewrites it and
    /// lands on a listed address ends the call there without running the override registered there.
    /// </summary>
    /// <param name="expectedReturnCs">The expected value of the CS register after the call.</param>
    /// <param name="expectedReturnIp">The expected value of the IP register after the call.</param>
    /// <param name="function">The function to call.</param>
    /// <param name="redirectedReturns">Addresses the call was observed to return to besides the expected one.</param>
    /// <returns><c>true</c> when the call ended at a redirected return address, <c>false</c> when it
    /// ended at the expected return address.</returns>
    public bool NearCallReturnedElsewhere(ushort expectedReturnCs, ushort expectedReturnIp, Func<int, Action> function, SegmentedAddress[] redirectedReturns) {
        return ExecuteCallEnsuringSameStack(expectedReturnCs, expectedReturnIp, expectedReturnCs, function, () => {
            Stack.Push16(expectedReturnIp);
            Action returnAction = function.Invoke(0);
            returnAction.Invoke();
        }, redirectedReturns);
    }

    /// <summary>
    /// Performs a near call with a 32-bit operand size, pushing a 32-bit return EIP onto the stack
    /// (matching <c>Stack.Push32</c> used by the interpreter for 32-bit near calls).
    /// </summary>
    /// <param name="expectedReturnCs">The expected value of the CS register after the call.</param>
    /// <param name="expectedReturnIp">The expected value of the IP register after the call.</param>
    /// <param name="function">The function to call.</param>
    public void NearCall32(ushort expectedReturnCs, ushort expectedReturnIp, Func<int, Action> function) {
        NearCall32ReturnedElsewhere(expectedReturnCs, expectedReturnIp, function, NoRedirectedReturns);
    }

    /// <summary>
    /// Performs a near call with a 32-bit operand size and tells whether it returned to one of the
    /// redirected return addresses in <paramref name="redirectedReturns"/> instead of the expected one.
    /// </summary>
    /// <param name="expectedReturnCs">The expected value of the CS register after the call.</param>
    /// <param name="expectedReturnIp">The expected value of the IP register after the call.</param>
    /// <param name="function">The function to call.</param>
    /// <param name="redirectedReturns">Addresses the call was observed to return to besides the expected one.</param>
    /// <returns><c>true</c> when the call ended at a redirected return address, <c>false</c> when it
    /// ended at the expected return address.</returns>
    public bool NearCall32ReturnedElsewhere(ushort expectedReturnCs, ushort expectedReturnIp, Func<int, Action> function, SegmentedAddress[] redirectedReturns) {
        return ExecuteCallEnsuringSameStack(expectedReturnCs, expectedReturnIp, expectedReturnCs, function, () => {
            Stack.Push32(expectedReturnIp);
            Action returnAction = function.Invoke(0);
            returnAction.Invoke();
        }, redirectedReturns);
    }

    /// <summary>
    /// Performs a far call.
    /// </summary>
    /// <param name="expectedReturnCs">The expected value of the CS register after the call.</param>
    /// <param name="expectedReturnIp">The expected value of the IP register after the call.</param>
    /// <param name="targetCs">The CS value of the callee, executed in for the duration of the call.</param>
    /// <param name="function">The function to call.</param>
    public void FarCall(ushort expectedReturnCs, ushort expectedReturnIp, ushort targetCs, Func<int, Action> function) {
        FarCallReturnedElsewhere(expectedReturnCs, expectedReturnIp, targetCs, function, NoRedirectedReturns);
    }

    /// <summary>
    /// Performs a far call and tells whether it returned to one of the redirected return addresses in
    /// <paramref name="redirectedReturns"/> instead of the expected one.
    /// </summary>
    /// <param name="expectedReturnCs">The expected value of the CS register after the call.</param>
    /// <param name="expectedReturnIp">The expected value of the IP register after the call.</param>
    /// <param name="targetCs">The CS value of the callee, executed in for the duration of the call.</param>
    /// <param name="function">The function to call.</param>
    /// <param name="redirectedReturns">Addresses the call was observed to return to besides the expected one.</param>
    /// <returns><c>true</c> when the call ended at a redirected return address, <c>false</c> when it
    /// ended at the expected return address.</returns>
    public bool FarCallReturnedElsewhere(ushort expectedReturnCs, ushort expectedReturnIp, ushort targetCs, Func<int, Action> function, SegmentedAddress[] redirectedReturns) {
        return ExecuteCallEnsuringSameStack(expectedReturnCs, expectedReturnIp, targetCs, function, () => {
            Stack.Push16(expectedReturnCs);
            Stack.Push16(expectedReturnIp);
            Action returnAction = function.Invoke(0);
            returnAction.Invoke();
        }, redirectedReturns);
    }

    /// <summary>
    /// Performs a far call with a 32-bit operand size, pushing the return address as a 32-bit far pointer
    /// (4-byte EIP + 2-byte CS + 2-byte padding) via <see cref="Stack.PushFarPointer32"/>, matching the
    /// interpreter's 32-bit far-call stack layout so <see cref="FarRet32"/> reads back the right values.
    /// </summary>
    /// <param name="expectedReturnCs">The expected value of the CS register after the call.</param>
    /// <param name="expectedReturnIp">The expected value of the IP register after the call.</param>
    /// <param name="targetCs">The CS value of the callee, executed in for the duration of the call.</param>
    /// <param name="function">The function to call.</param>
    public void FarCall32(ushort expectedReturnCs, ushort expectedReturnIp, ushort targetCs, Func<int, Action> function) {
        FarCall32ReturnedElsewhere(expectedReturnCs, expectedReturnIp, targetCs, function, NoRedirectedReturns);
    }

    /// <summary>
    /// Performs a far call with a 32-bit operand size and tells whether it returned to one of the
    /// redirected return addresses in <paramref name="redirectedReturns"/> instead of the expected one.
    /// </summary>
    /// <param name="expectedReturnCs">The expected value of the CS register after the call.</param>
    /// <param name="expectedReturnIp">The expected value of the IP register after the call.</param>
    /// <param name="targetCs">The CS value of the callee, executed in for the duration of the call.</param>
    /// <param name="function">The function to call.</param>
    /// <param name="redirectedReturns">Addresses the call was observed to return to besides the expected one.</param>
    /// <returns><c>true</c> when the call ended at a redirected return address, <c>false</c> when it
    /// ended at the expected return address.</returns>
    public bool FarCall32ReturnedElsewhere(ushort expectedReturnCs, ushort expectedReturnIp, ushort targetCs, Func<int, Action> function, SegmentedAddress[] redirectedReturns) {
        return ExecuteCallEnsuringSameStack(expectedReturnCs, expectedReturnIp, targetCs, function, () => {
            Stack.PushFarPointer32(new SegmentedAddress32(expectedReturnCs, expectedReturnIp));
            Action returnAction = function.Invoke(0);
            returnAction.Invoke();
        }, redirectedReturns);
    }

    /// <summary>
    /// Performs a typed near call: pushes <paramref name="expectedReturnIp"/> on the stack exactly like
    /// <see cref="NearCall(ushort, ushort, Func{int, Action})"/>, runs the typed callee and then requires
    /// CS:IP to be the expected return address.
    /// </summary>
    /// <param name="expectedReturnCs">The expected value of the CS register after the call.</param>
    /// <param name="expectedReturnIp">The expected value of the IP register after the call.</param>
    /// <param name="typedCall">The typed callee. It ends with <see cref="NearReturn"/> or
    /// <see cref="FarReturn"/>, which pops the pushed return address, then returns its value.</param>
    /// <typeparam name="T">The return type of the typed callee.</typeparam>
    /// <returns>The value the typed callee returned.</returns>
    /// <remarks>
    /// No dispatcher identity is taken and no override chain runs: typed calls are only emitted for callees
    /// without a cyclic jump that always return to their caller, and every call helper inside a typed callee
    /// opens its own dispatcher scope. A <see cref="CallStackUnwindException"/> is not caught: a typed
    /// callee that returns to a different address than pushed unwinds to the outer call expecting it.
    /// Typed callees are eligible only when their return exits provably leave SP at its entry delta
    /// and never write their own return frame, so the unwind is a safety net for the cases that
    /// defeat those static checks at run time (for example an aliasing write through another
    /// segment), not the normal path.
    /// </remarks>
    /// <exception cref="UnrecoverableException">The typed callee did not return to the expected return address.</exception>
    public T NearCall<T>(ushort expectedReturnCs, ushort expectedReturnIp, Func<T> typedCall) {
        return ExecuteTypedCall(expectedReturnCs, expectedReturnIp, expectedReturnCs, false, typedCall);
    }

    /// <summary>
    /// Performs a typed near call of a callee that returns no value: same stack contract as
    /// <see cref="NearCall{T}(ushort, ushort, Func{T})"/>.
    /// </summary>
    /// <param name="expectedReturnCs">The expected value of the CS register after the call.</param>
    /// <param name="expectedReturnIp">The expected value of the IP register after the call.</param>
    /// <param name="typedCall">The typed callee, ending with <see cref="NearReturn"/> or <see cref="FarReturn"/>.</param>
    /// <exception cref="UnrecoverableException">The typed callee did not return to the expected return address.</exception>
    public void NearCall(ushort expectedReturnCs, ushort expectedReturnIp, Action typedCall) {
        ExecuteTypedCall(expectedReturnCs, expectedReturnIp, expectedReturnCs, false, typedCall);
    }

    /// <summary>
    /// Performs a typed far call: pushes <paramref name="expectedReturnCs"/> then
    /// <paramref name="expectedReturnIp"/> on the stack exactly like
    /// <see cref="FarCall(ushort, ushort, ushort, Func{int, Action})"/>, sets CS to
    /// <paramref name="targetCs"/>, runs the typed callee and then requires CS:IP to be the expected return
    /// address.
    /// </summary>
    /// <param name="expectedReturnCs">The expected value of the CS register after the call.</param>
    /// <param name="expectedReturnIp">The expected value of the IP register after the call.</param>
    /// <param name="targetCs">The CS value of the callee, executed in for the duration of the call.</param>
    /// <param name="typedCall">The typed callee, ending with <see cref="NearReturn"/> or <see cref="FarReturn"/>.</param>
    /// <typeparam name="T">The return type of the typed callee.</typeparam>
    /// <returns>The value the typed callee returned.</returns>
    /// <exception cref="UnrecoverableException">The typed callee did not return to the expected return address.</exception>
    public T FarCall<T>(ushort expectedReturnCs, ushort expectedReturnIp, ushort targetCs, Func<T> typedCall) {
        return ExecuteTypedCall(expectedReturnCs, expectedReturnIp, targetCs, true, typedCall);
    }

    /// <summary>
    /// Performs a typed far call of a callee that returns no value: same stack contract as
    /// <see cref="FarCall{T}(ushort, ushort, ushort, Func{T})"/>.
    /// </summary>
    /// <param name="expectedReturnCs">The expected value of the CS register after the call.</param>
    /// <param name="expectedReturnIp">The expected value of the IP register after the call.</param>
    /// <param name="targetCs">The CS value of the callee, executed in for the duration of the call.</param>
    /// <param name="typedCall">The typed callee, ending with <see cref="NearReturn"/> or <see cref="FarReturn"/>.</param>
    /// <exception cref="UnrecoverableException">The typed callee did not return to the expected return address.</exception>
    public void FarCall(ushort expectedReturnCs, ushort expectedReturnIp, ushort targetCs, Action typedCall) {
        ExecuteTypedCall(expectedReturnCs, expectedReturnIp, targetCs, true, typedCall);
    }

    /// <summary>
    /// Pops the stacked near return address (IP) immediately, with the same operand bytes as a
    /// <c>ret n</c>: the return of a typed callee, which has no <see cref="Action"/> to return.
    /// </summary>
    /// <param name="numberOfBytesToPop">Bytes to pop after the return address (0 for a plain <c>ret</c>).</param>
    public void NearReturn(ushort numberOfBytesToPop) => ReturnOperationsHelper.NearRet16(numberOfBytesToPop);

    /// <summary>
    /// Pops the stacked near return address (IP) immediately, the plain <c>ret</c> of a typed callee that
    /// pops no parameter bytes.
    /// </summary>
    public void NearReturn() => NearReturn(0);

    /// <summary>
    /// Pops the stacked far return address (CS:IP) immediately, with the same operand bytes as a
    /// <c>retf n</c>: the return of a typed callee, which has no <see cref="Action"/> to return.
    /// </summary>
    /// <param name="numberOfBytesToPop">Bytes to pop after the return address (0 for a plain <c>retf</c>).</param>
    public void FarReturn(ushort numberOfBytesToPop) => ReturnOperationsHelper.FarRet16(numberOfBytesToPop);

    /// <summary>
    /// Pops the stacked far return address (CS:IP) immediately, the plain <c>retf</c> of a typed callee
    /// that pops no parameter bytes.
    /// </summary>
    public void FarReturn() => FarReturn(0);

    /// <summary>
    /// The no-op <see cref="Action"/> an entry wrapper returns after its typed method already performed
    /// the return (<see cref="NearReturn"/> / <see cref="FarReturn"/> popped the stacked return address and
    /// set CS:IP). Returning it is exact: the wrapper adds no frame and the action does nothing.
    /// </summary>
    public static Action Returned { get; } = () => { };

    /// <summary>
    /// Runs a typed call dispatch: pushes the return frame the untyped helpers push, then requires the
    /// callee to have returned to the expected return address with the stack at or above the level it had
    /// before the pushes.
    /// </summary>
    private T ExecuteTypedCall<T>(ushort expectedReturnCs, ushort expectedReturnIp, ushort targetCs,
        bool isFar, Func<T> typedCall) {
        uint expectedStackAddress = PrepareTypedCall(expectedReturnCs, expectedReturnIp, targetCs, isFar);
        T result = typedCall.Invoke();
        CheckTypedCallReturn(expectedReturnCs, expectedReturnIp, expectedStackAddress);
        return result;
    }

    private void ExecuteTypedCall(ushort expectedReturnCs, ushort expectedReturnIp, ushort targetCs,
        bool isFar, Action typedCall) {
        uint expectedStackAddress = PrepareTypedCall(expectedReturnCs, expectedReturnIp, targetCs, isFar);
        typedCall.Invoke();
        CheckTypedCallReturn(expectedReturnCs, expectedReturnIp, expectedStackAddress);
    }

    private uint PrepareTypedCall(ushort expectedReturnCs, ushort expectedReturnIp, ushort targetCs, bool isFar) {
        uint expectedStackAddress = State.StackPhysicalAddress;
        if (isFar) {
            Stack.Push16(expectedReturnCs);
        }
        Stack.Push16(expectedReturnIp);
        State.CS = targetCs;
        State.IP = expectedReturnIp;
        return expectedStackAddress;
    }

    private void CheckTypedCallReturn(ushort expectedReturnCs, ushort expectedReturnIp, uint expectedStackAddress) {
        if (State.StackPhysicalAddress < expectedStackAddress) {
            throw FailAsUntested($"Typed call consumed the stack down to {State.StackPhysicalAddress:X5}, below {expectedStackAddress:X5}");
        }
        if (State.CS != expectedReturnCs || State.IP != expectedReturnIp) {
            SegmentedAddress actual = new(State.CS, State.IP);
            throw new CallStackUnwindException(actual,
                $"Typed call returned to {actual} instead of {expectedReturnCs:X4}:{expectedReturnIp:X4}");
        }
    }

    /// <summary>
    /// Call the given callback number
    /// </summary>
    /// <param name="callbackNumber">The callback identifier.</param>
    public void Callback(ushort callbackNumber) {
        _callbackHandler.RunFromOverriden(callbackNumber);
    }

    /// <summary>
    /// Runs the callback whose instruction sits at <paramref name="cs"/>:<paramref name="ip"/> and tells
    /// whether the handler left CS:IP somewhere else.
    /// CS:IP is set first so handler code that reads it sees the callback's own address, like the interpreter does.
    /// A handler that stopped the machine (DOS terminate, TSR) exits here, because generated code would otherwise
    /// keep running the instruction after the callback.
    /// </summary>
    /// <param name="cs">The segment of the callback instruction.</param>
    /// <param name="ip">The offset of the callback instruction.</param>
    /// <param name="callbackNumber">The callback identifier.</param>
    /// <returns><c>true</c> when the handler moved CS:IP away from the callback instruction (DOS EXEC, terminate, TSR).</returns>
    /// <exception cref="HaltRequestedException">The callback stopped the machine.</exception>
    public bool CallbackMovedCsIp(ushort cs, ushort ip, ushort callbackNumber) {
        State.CS = cs;
        State.IP = ip;
        _callbackHandler.RunFromOverriden(callbackNumber);
        if (!State.IsRunning) {
            Exit();
        }
        return State.CS != cs || State.IP != ip;
    }

    /// <summary>
    /// Runs the callback whose instruction sits at <paramref name="cs"/>:<paramref name="ip"/>, for callbacks
    /// whose handler is known to keep CS:IP at the callback instruction. A handler that moved CS:IP elsewhere
    /// fails as untested; generated code emits a dispatch over the observed targets instead (see
    /// <see cref="CallbackMovedCsIp"/>).
    /// </summary>
    /// <param name="cs">The segment of the callback instruction.</param>
    /// <param name="ip">The offset of the callback instruction.</param>
    /// <param name="callbackNumber">The callback identifier.</param>
    /// <exception cref="HaltRequestedException">The callback stopped the machine.</exception>
    /// <exception cref="UnrecoverableException">The callback moved CS:IP away from the callback instruction.</exception>
    public void Callback(ushort cs, ushort ip, ushort callbackNumber) {
        if (CallbackMovedCsIp(cs, ip, callbackNumber)) {
            throw FailAsUntested($"Untested callback CS:IP change at {cs:X4}:{ip:X4}: moved to {State.CS:X4}:{State.IP:X4}");
        }
    }

    /// <summary>
    /// Performs an interrupt call by executing the given function and returning to the specified return address.
    /// </summary>
    /// <param name="expectedReturnCs">The expected value of the CS register after the interrupt call.</param>
    /// <param name="expectedReturnIp">The expected value of the IP register after the interrupt call.</param>
    /// <param name="targetCs">The CS value of the interrupt handler, executed in for the duration of the call.</param>
    /// <param name="function">The function to execute as part of the interrupt call.</param>
    public void InterruptCall(ushort expectedReturnCs, ushort expectedReturnIp, ushort targetCs, Func<int, Action> function) {
        ExecuteInterruptCall(expectedReturnCs, expectedReturnIp, targetCs, function, NoRedirectedReturns);
    }

    /// <summary>
    /// Performs an interrupt call and tells whether the handler returned to one of the addresses the
    /// call was observed to be redirected to instead of
    /// <paramref name="expectedReturnCs"/>:<paramref name="expectedReturnIp"/>.
    /// The return address pushed on the stack is still the expected one, so handlers that read operand
    /// bytes from it keep working; a handler that rewrites the stacked return address and lands on a
    /// listed address ends the call there without running the override registered there.
    /// </summary>
    /// <param name="expectedReturnCs">The expected value of the CS register after the interrupt call.</param>
    /// <param name="expectedReturnIp">The expected value of the IP register after the interrupt call.</param>
    /// <param name="vectorNumber">The vector number to call for the interrupt.</param>
    /// <param name="redirectedReturns">Addresses the call was observed to return to besides the expected one.</param>
    /// <returns><c>true</c> when the call ended at a redirected return address, <c>false</c> when it
    /// ended at the expected return address.</returns>
    /// <exception cref="UnrecoverableException">If the interrupt vector number is not recognized.</exception>
    public bool InterruptCallReturnedElsewhere(ushort expectedReturnCs, ushort expectedReturnIp, byte vectorNumber, SegmentedAddress[] redirectedReturns) {
        SegmentedAddress target = Machine.InterruptVectorTable[vectorNumber];
        Func<int, Action>? function = SearchFunctionOverride(target);
        if (function is null) {
            throw FailAsUntested($"Could not find an override at address {target}");
        }

        return ExecuteInterruptCall(expectedReturnCs, expectedReturnIp, target.Segment, function, redirectedReturns);
    }

    private bool ExecuteInterruptCall(ushort expectedReturnCs, ushort expectedReturnIp, ushort targetCs, Func<int, Action> function, SegmentedAddress[] redirectedReturns) {
        return ExecuteCallEnsuringSameStack(expectedReturnCs, expectedReturnIp, targetCs, function, () => {
            Stack.Push16(State.Flags.FlagRegister16);
            InterruptFlag = false;
            Stack.Push16(expectedReturnCs);
            Stack.Push16(expectedReturnIp);
            Action returnAction = function.Invoke(0);
            returnAction.Invoke();
        }, redirectedReturns);
    }

    /// <summary>
    /// Performs an interrupt call.
    /// </summary>
    /// <param name="expectedReturnCs">The excepted value of the CS register after the interrupt call.</param>
    /// <param name="expectedReturnIp">The excepted value of the IP register after the interrupt call.</param>
    /// <param name="vectorNumber">The vector number to call for the interrupt.</param>
    /// <exception cref="UnrecoverableException">If the interrupt vector number is not recognized.</exception>
    public void InterruptCall(ushort expectedReturnCs, ushort expectedReturnIp, byte vectorNumber) {
        InterruptCallReturnedElsewhere(expectedReturnCs, expectedReturnIp, vectorNumber, NoRedirectedReturns);
    }

    /// <summary>
    /// Checks that the interrupt vector <paramref name="vectorNumber"/> still points to the stub of
    /// the interrupt service the emulator installed at startup. A program that hooked the vector
    /// points it elsewhere, and calling the built-in service implementation would be wrong.
    /// </summary>
    /// <param name="vectorNumber">The interrupt vector number to check.</param>
    /// <exception cref="UnrecoverableException">The vector does not point to the installed stub.</exception>
    public void CheckInterruptServiceVector(byte vectorNumber) {
        SegmentedAddress current = Machine.InterruptVectorTable[vectorNumber];
        if (!Machine.InstalledInterruptHandlers.TryGetInstalledAddress(vectorNumber, out SegmentedAddress installed)
            || installed != current) {
            throw FailAsUntested(
                $"Interrupt vector 0x{vectorNumber:X2} no longer points to the built-in handler at {installed}: it points to {current}");
        }
    }

    /// <summary>
    /// Performs an interrupt call after checking the vector still points to the built-in handler's
    /// stub. Used by the typed service helpers before writing their parameters into the registers.
    /// </summary>
    /// <param name="expectedReturnCs">The expected value of the CS register after the interrupt call.</param>
    /// <param name="expectedReturnIp">The expected value of the IP register after the interrupt call.</param>
    /// <param name="vectorNumber">The vector number to call for the interrupt.</param>
    /// <exception cref="UnrecoverableException">The vector was hooked, or the interrupt vector number is not recognized.</exception>
    public void InterruptServiceCall(ushort expectedReturnCs, ushort expectedReturnIp, byte vectorNumber) {
        CheckInterruptServiceVector(vectorNumber);
        InterruptCall(expectedReturnCs, expectedReturnIp, vectorNumber);
    }

    /// <summary>
    /// Returns the installed interrupt handler of type <typeparamref name="THandler"/>.
    /// </summary>
    /// <typeparam name="THandler">The handler type.</typeparam>
    /// <exception cref="UnrecoverableException">No handler of the type is installed.</exception>
    internal THandler GetInterruptHandler<THandler>() where THandler : class, IInterruptHandler {
        return Machine.InstalledInterruptHandlers.Handlers.Values.OfType<THandler>()
            .FirstOrDefault() ?? throw FailAsUntested(
                $"No installed interrupt handler of type {typeof(THandler).Name}");
    }

    /// <summary>
    /// Runs the declared <see cref="ServiceExecutionPath.Direct"/> service <paramref name="service"/>
    /// of <paramref name="handler"/> without executing its stub bytes: after checking the vector and
    /// the stub bytes themselves, the same frame as <see cref="ExecuteInterruptCall"/> is pushed
    /// (FLAGS, IF cleared, return address), <paramref name="invoke"/> runs with CS:IP at the
    /// installed callback address, <paramref name="writeFrameFlags"/> updates the output flag bits
    /// the service promises on the stacked FLAGS word, and the frame is popped by an interrupt
    /// return validated like a typed call return.
    /// </summary>
    /// <typeparam name="T">The result type of the service implementation.</typeparam>
    /// <param name="expectedReturnCs">The expected value of the CS register after the call.</param>
    /// <param name="expectedReturnIp">The expected value of the IP register after the call.</param>
    /// <param name="handler">The installed interrupt handler the service belongs to.</param>
    /// <param name="service">The declared entry of the called service.</param>
    /// <param name="invoke">The service implementation invocation.</param>
    /// <param name="writeFrameFlags">Writes the stacked output flag bits from the result.</param>
    /// <exception cref="UnrecoverableException">The service is not declared
    /// <see cref="ServiceExecutionPath.Direct"/>, the vector was hooked, or the installed stub
    /// bytes differ from the callback-plus-iret shape the call replaces.</exception>
    internal T DirectInterruptServiceCall<T>(ushort expectedReturnCs, ushort expectedReturnIp,
        IInterruptHandler handler, InterruptServiceEntry service, Func<T> invoke,
        Action<T> writeFrameFlags) {
        CheckInterruptServiceVector(handler.VectorNumber);
        if (service.ExecutionPath != ServiceExecutionPath.Direct) {
            throw FailAsUntested(
                $"Service {service.Name} of vector 0x{handler.VectorNumber:X2} is not declared Direct");
        }
        SegmentedAddress stub = Machine.InterruptVectorTable[handler.VectorNumber];
        byte[] expectedBytes = [0xFE, 0x38, handler.VectorNumber, 0x00, 0xCF];
        for (int i = 0; i < expectedBytes.Length; i++) {
            byte actual = Memory.UInt8[stub.Segment, (ushort)(stub.Offset + i)];
            if (actual != expectedBytes[i]) {
                throw FailAsUntested(
                    $"Stub byte {i} of the direct service {service.Name} at {stub}: "
                    + $"expected 0x{expectedBytes[i]:X2}, found 0x{actual:X2}");
            }
        }

        uint expectedStackAddress = State.StackPhysicalAddress;
        Stack.Push16(State.Flags.FlagRegister16);
        InterruptFlag = false;
        Stack.Push16(expectedReturnCs);
        Stack.Push16(expectedReturnIp);
        State.CS = stub.Segment;
        State.IP = stub.Offset;
        T result = invoke();
        writeFrameFlags(result);
        ReturnOperationsHelper.InterruptRet();
        CheckTypedCallReturn(expectedReturnCs, expectedReturnIp, expectedStackAddress);
        return result;
    }

    /// <summary>
    /// Performs an interrupt call to a handler of the generated program itself. The generated code
    /// calls <paramref name="handler"/> directly because the observed vector target was the handler;
    /// when the vector no longer points to <paramref name="handlerCs"/>:<paramref name="handlerIp"/>
    /// the call cannot deliver that handler and fails as untested. Otherwise the same
    /// pushed-FLAGS / cleared-IF / pushed-return-address frame as
    /// <see cref="InterruptCall(ushort, ushort, byte)"/> applies.
    /// </summary>
    /// <param name="expectedReturnCs">The expected value of the CS register after the interrupt call.</param>
    /// <param name="expectedReturnIp">The expected value of the IP register after the interrupt call.</param>
    /// <param name="vectorNumber">The vector number to call for the interrupt.</param>
    /// <param name="handlerCs">The CS of the handler entry the vector must point to.</param>
    /// <param name="handlerIp">The IP of the handler entry the vector must point to.</param>
    /// <param name="handler">The generated method of the handler, executed as part of the interrupt call.</param>
    /// <exception cref="UnrecoverableException">If the interrupt vector no longer points to the handler.</exception>
    public void InterruptCall(ushort expectedReturnCs, ushort expectedReturnIp, byte vectorNumber,
        ushort handlerCs, ushort handlerIp, Func<int, Action> handler) {
        SegmentedAddress target = Machine.InterruptVectorTable[vectorNumber];
        if (target.Segment != handlerCs || target.Offset != handlerIp) {
            throw FailAsUntested($"Interrupt vector 0x{vectorNumber:X2} points to {target}, not to the handler {handlerCs:X4}:{handlerIp:X4} the generated code calls");
        }
        ExecuteInterruptCall(expectedReturnCs, expectedReturnIp, handlerCs, handler, NoRedirectedReturns);
    }

    /// <summary>
    /// Performs the CPU-fault entry sequence used by generated code: pushes the flags register and the
    /// faulting instruction's address as the return frame, clears the interrupt flag, and sets CS:IP to the
    /// resolved fault handler target. Control transfer into the handler partition happens at the call site.
    /// </summary>
    /// <param name="faultingInstructionCs">The CS of the instruction that faulted, pushed as the return segment.</param>
    /// <param name="faultingInstructionIp">The IP of the instruction that faulted, pushed as the return offset.</param>
    /// <param name="target">The fault handler address resolved through the interrupt vector table.</param>
    public void EnterCpuFaultHandler(ushort faultingInstructionCs, ushort faultingInstructionIp, SegmentedAddress target) {
        Stack.Push16(State.Flags.FlagRegister16);
        Stack.PushSegmentedAddress(new SegmentedAddress(faultingInstructionCs, faultingInstructionIp));
        InterruptFlag = false;
        CS = target.Segment;
        IP = target.Offset;
    }

    /// <summary>
    /// Dispatches a CPU fault to the matching generated handler partition.
    /// </summary>
    /// <param name="exception">The CPU exception that triggered the fault.</param>
    /// <param name="faultCs">The CS of the faulting instruction.</param>
    /// <param name="faultIp">The IP of the faulting instruction.</param>
    /// <param name="targets">The list of fault handler targets to match against the live IVT.</param>
    /// <returns>The return action from the matching handler partition.</returns>
    /// <exception cref="UnrecoverableException">Thrown when no target matches the live IVT handler.</exception>
    public Action DispatchCpuFault(CpuException exception, ushort faultCs, ushort faultIp, IReadOnlyList<CpuFaultTarget> targets) {
        SegmentedAddress handler = _interruptVectorTable[exception.InterruptVector];
        foreach (CpuFaultTarget target in targets) {
            if (target.Handler == handler) {
                EnterCpuFaultHandler(faultCs, faultIp, handler);
                return target.Invoke();
            }
        }
        throw FailAsUntested($"Untested CPU fault target {handler} at {faultCs:X4}:{faultIp:X4}");
    }

    /// <summary>
    /// Returns the C# function override, or <c>null</c> if not found.
    /// </summary>
    /// <param name="target">The <see cref="SegmentedAddress"/> where the function is defined.</param>
    /// <returns>The C# function override, or <c>null</c> if not found.</returns>
    public Func<int, Action>? SearchFunctionOverride(SegmentedAddress target) {
        if (!FunctionInformations.TryGetValue(target,
                out FunctionInformation? functionInformation)) {
            return null;
        }

        return functionInformation.FunctionOverride;
    }

    private static readonly SegmentedAddress[] NoRedirectedReturns = [];

    /// <summary>
    /// Runs a call dispatch and its override chain, then tells whether the call ended at one of
    /// <paramref name="redirectedReturns"/> (<c>true</c>) or at the expected return address
    /// (<c>false</c>).
    /// </summary>
    private bool ExecuteCallEnsuringSameStack(ushort expectedReturnCs, ushort expectedReturnIp,
        ushort targetCs, Func<int, Action> function, Action action, SegmentedAddress[] redirectedReturns) {
        uint expectedStackAddress = State.StackPhysicalAddress;
        // CS is set to the callee's segment for the duration of the call: this is the segment the emulated
        // CPU runs in while inside the callee, so reads of CS (e.g. a "push CS" idiom) observe the right value.
        // For near calls targetCs equals expectedReturnCs so CS is unchanged. IP is set to the expected return
        // offset as a fallback for the chain below; generated code never reads State.IP mid-call
        // (CheckExternalEvents takes the offset explicitly), and the callee's return action overwrites CS:IP
        // with the actual return address before the chain runs.
        State.CS = targetCs;
        State.IP = expectedReturnIp;
        SegmentedAddress expectedReturn = new SegmentedAddress(expectedReturnCs, expectedReturnIp);
        ExecuteCallConsumingUnwindTo(function, action, expectedReturn, expectedStackAddress, redirectedReturns);
        CSharpCallChainCaller caller = new(this, expectedReturn, expectedStackAddress, redirectedReturns);
        Machine.CfgCpu.FunctionHandlerInUse.RunOverrideChain(caller, null);
        return caller.EndedAtRedirectedReturn;
    }

    /// <summary>
    /// Runs a dispatch started by a call and consumes an unwind that landed on the call's expected return
    /// address, which is how code that unwinds its own call stack frame returns. Any other unwind is not
    /// this call's return, so it keeps propagating towards the frame that can interpret it.
    /// </summary>
    /// <param name="function">Starting point of the dispatch in the jump dispatcher.</param>
    /// <param name="action">Dispatch to run.</param>
    /// <param name="expectedReturn">Address the call that started the dispatch expects to return to.</param>
    /// <param name="expectedStackAddress">Physical stack address before the call's pushes.</param>
    /// <param name="redirectedReturns">Other addresses the call accepts as its return.</param>
    private void ExecuteCallConsumingUnwindTo(Func<int, Action> function, Action action, SegmentedAddress expectedReturn,
        uint expectedStackAddress, SegmentedAddress[] redirectedReturns) {
        try {
            ExecuteCall(function, action);
        } catch (CallStackUnwindException exception) when (IsAcceptedUnwindTarget(exception.Target, expectedReturn, expectedStackAddress, redirectedReturns)) {
            // The unwound code already left CS:IP at an accepted return address, so the validation loop
            // of the surrounding call sees it and does not run.
        }
    }

    private bool IsAcceptedUnwindTarget(SegmentedAddress target, SegmentedAddress expectedReturn,
        uint expectedStackAddress, SegmentedAddress[] redirectedReturns) {
        if (target == expectedReturn) {
            return true;
        }
        return CSharpCallChainCaller.IsRedirectedReturn(redirectedReturns, target, State.StackPhysicalAddress, expectedStackAddress);
    }

    /// <summary>
    /// Logs a nested override call: once at warning level per (expected return, landing) pair of this
    /// helper, then at debug level, since each occurrence grows the C# stack once more.
    /// </summary>
    private void LogNestedOverrideCall(SegmentedAddress expectedReturn, SegmentedAddress actualReturn,
        uint expectedStackAddress, uint actualStackAddress, string? functionName) {
        if (_warnedNestedOverrideCalls.Add((expectedReturn, actualReturn))) {
            if (_loggerService.IsEnabled(LogLevel.Warning)) {
                _loggerService.LogWarning(
                    "The call expecting to return to {ExpectedReturn} continued at {ActualReturn} (stack {ActualStackAddress}, before the call {ExpectedStackAddress}). Running {FunctionName} inside the call: the C# stack grows each time this happens. Logged once per call and target.",
                    expectedReturn, actualReturn, actualStackAddress, expectedStackAddress, functionName);
            }
        } else if (_loggerService.IsEnabled(LogLevel.Debug)) {
            _loggerService.LogDebug(
                "The call expecting to return to {ExpectedReturn} continued at {ActualReturn} again. Running {FunctionName} inside the call.",
                expectedReturn, actualReturn, functionName);
        }
    }

    /// <summary>
    /// Executes the given action with the specified function as starting point in the jump dispatcher.
    /// </summary>
    /// <param name="function">The function to set as starting point in the jump dispatcher.</param>
    /// <param name="action">The action to execute.</param>
    /// <remarks>
    /// This method temporarily replaces the current jump dispatcher with a new instance that has the specified function
    /// as its starting point. After the action has been executed, the original jump dispatcher is restored.
    /// </remarks>
    private void ExecuteCall(Func<int, Action> function, Action action) {
        JumpDispatcher currentJumpDispatcher = JumpDispatcher;
        // Ensure the jump dispatcher has the function we are calling as starting point
        JumpDispatcher = JumpDispatcher.CreateNew(function);
        try {
            action.Invoke();
        } finally {
            // Restored even when the call throws (e.g. an unwind to an outer frame), so an exception leaves
            // the dispatcher as it found it.
            JumpDispatcher = currentJumpDispatcher;
        }
    }

    /// <summary>
    /// Overrides the machine code at the specified segment and offset with a new implementation provided by the <paramref name="renamedOverride"/> function.
    /// </summary>
    /// <param name="segment">The segment of the instruction to override.</param>
    /// <param name="offset">The offset of the instruction to override.</param>
    /// <param name="renamedOverride">An action that provides the new implementation to use for the instruction.</param>
    public void OverrideInstruction(ushort segment, ushort offset, Func<Action> renamedOverride) {
        AddressBreakPoint breakPoint = new(
            BreakPointType.CPU_EXECUTION_ADDRESS,
            MemoryUtils.ToPhysicalAddress(
                segment,
                offset),
            (_, _) => renamedOverride.Invoke().Invoke()
            , false);
        EmulatorBreakpointsManager.ToggleBreakPoint(breakPoint, true);
    }

    /// <summary>
    /// Executes the specified action on top of the instruction at the specified segment and offset.
    /// </summary>
    /// <param name="segment">The segment of the instruction to execute the action on.</param>
    /// <param name="offset">The offset of the instruction to execute the action on.</param>
    /// <param name="action">The action to execute on top of the instruction.</param>
    public void DoOnTopOfInstruction(ushort segment, ushort offset, Action action) {
        AddressBreakPoint breakPoint = new(
            BreakPointType.CPU_EXECUTION_ADDRESS,
            MemoryUtils.ToPhysicalAddress(
                segment,
                offset),
            (_, _) => action.Invoke()
            , false);
        EmulatorBreakpointsManager.ToggleBreakPoint(breakPoint, true);
    }

    /// <summary>
    /// Executes the specified action when the byte at the specified segment and offset is written to.
    /// </summary>
    /// <param name="segment">The segment of the memory location to watch.</param>
    /// <param name="offset">The offset of the memory location to watch.</param>
    /// <param name="action">The action to execute when the memory location is written to.</param>
    public void DoOnMemoryWrite(ushort segment, ushort offset, Action action) {
        AddressBreakPoint breakPoint = new(
            BreakPointType.MEMORY_WRITE,
            MemoryUtils.ToPhysicalAddress(segment, offset),
            (_, _) => action.Invoke()
            , false);
        EmulatorBreakpointsManager.ToggleBreakPoint(breakPoint, true);
    }

    /// <summary>
    /// Executes the specified action when the byte at the specified segment and offset is written to.
    /// </summary>
    /// <param name="segment">The segment of the memory location to watch.</param>
    /// <param name="offset">The offset of the memory location to watch.</param>
    /// <param name="action">The action to execute when the memory location is written to.</param>
    public void DoOnMemoryRead(ushort segment, ushort offset, Action action) {
        AddressBreakPoint breakPoint = new(
            BreakPointType.MEMORY_READ,
            MemoryUtils.ToPhysicalAddress(segment, offset),
            (_, _) => action.Invoke()
            , false);
        EmulatorBreakpointsManager.ToggleBreakPoint(breakPoint, true);
    }

    /// <summary>
    /// Checks if the vtable contains the expected segment and offset values.
    /// </summary>
    /// <param name="segmentRegisterIndex">The index of the segment register to use.</param>
    /// <param name="offset">The offset of the vtable entry to check.</param>
    /// <param name="expectedSegment">The expected segment value.</param>
    /// <param name="expectedOffset">The expected offset value.</param>
    /// <exception cref="UnrecoverableException">Thrown when the found segment or offset value doesn't match the expected values.</exception>
    public void CheckVtableContainsExpected(
        int segmentRegisterIndex,
        ushort offset,
        ushort expectedSegment,
        ushort expectedOffset) {
        uint address = MemoryUtils.ToPhysicalAddress(State.SegmentRegisters.UInt16[segmentRegisterIndex], offset);
        (ushort foundSegment, ushort foundOffset) = Memory.SegmentedAddress16[address];
        if (foundOffset != expectedOffset || foundSegment != expectedSegment) {
            throw FailAsUntested(
                $"Call table value changed, we would not call the method the game is calling. Expected: {new SegmentedAddress(expectedSegment, expectedOffset)} found: {new SegmentedAddress(foundSegment, foundOffset)}");
        }
    }

    /// <summary>
    /// Call this in your override when you re-implement a function with a branch that seems to be never
    /// reached.
    /// <param name="message">The error message for the <see cref="UnrecoverableException"/></param>
    /// <returns>An new instance of <see cref="UnrecoverableException"/> that you should throw.</returns>
    /// </summary>
    public UnrecoverableException FailAsUntested(string message) {
        string error =
            $"Untested code reached, please tell us how to reach this state. Here is the message: {message}. Here is the Machine stack: {State}";
        if (_loggerService.IsEnabled(LogLevel.Error)) {
            _loggerService.LogError("{Error}", error);
        }

        return new UnrecoverableException(error);
    }

    /// <summary>
    /// Compares the in-memory bytes at the given segmented address against a self-modifying-code selector
    /// signature, reusing the exact primitives the interpreter uses (<see cref="IMemory.GetSlice"/> over a
    /// linear slice and <see cref="Signature.ListEquivalent(IList{byte})"/>, where null entries are
    /// wildcards). This keeps generated selector dispatch provably identical to the execution oracle
    /// (<c>SelectorNode.GetNextSuccessor</c> / <c>MemoryInstructionMatcher</c>): no MMU translation, no
    /// segment-limit fault, and no 16-bit offset wraparound.
    /// </summary>
    /// <param name="segment">The segment of the address whose bytes are compared.</param>
    /// <param name="offset">The offset of the address whose bytes are compared.</param>
    /// <param name="signature">The expected byte pattern; null entries match any byte.</param>
    /// <returns><c>true</c> when every non-null signature byte matches memory; otherwise <c>false</c>.</returns>
    public bool SelectorSignatureMatches(ushort segment, ushort offset, byte?[] signature) {
        uint linearAddress = MemoryUtils.ToPhysicalAddress(segment, offset);
        IList<byte> bytes = Memory.GetSlice((int)linearAddress, signature.Length);
        return new Signature(ImmutableList.CreateRange(signature)).ListEquivalent(bytes);
    }

    /// <summary>
    /// Verifies that a single speculative instruction at the given segmented address still matches the
    /// signature decoded at exploration time. The generated code calls this immediately before the
    /// instruction's body so that self-modifying code performed by an earlier instruction in the same block
    /// is detected before the modified instruction's decode-time-baked body executes. If memory no longer
    /// matches, the speculative decode was wrong and execution falls back to <see cref="FailAsUntested"/>.
    /// Reads via raw <c>segment*16+offset</c> (no MMU translation) to stay identical to the interpreter oracle that recorded the signature; must change in lockstep with it, never alone.
    /// </summary>
    /// <param name="segment">The segment of the speculative instruction.</param>
    /// <param name="offset">The offset of the speculative instruction.</param>
    /// <param name="runSignature">The signature of the speculative instruction; null entries are wildcards for fields the decoder reads from memory at execution time.</param>
    /// <exception cref="UnrecoverableException">Thrown when memory at the instruction address does not match the expected signature.</exception>
    public void VerifySpeculativeEntryOrFail(ushort segment, ushort offset, byte?[] runSignature) {
        uint linearAddress = MemoryUtils.ToPhysicalAddress(segment, offset);
        IList<byte> bytes = Memory.GetSlice((int)linearAddress, runSignature.Length);
        if (!new Signature(ImmutableList.CreateRange(runSignature)).ListEquivalent(bytes)) {
            throw FailAsUntested($"Speculative code at {segment:X4}:{offset:X4} no longer matches memory");
        }
    }

    /// <summary>
    /// Verifies the signatures of a straight-line run of speculative instructions, checked in memory
    /// order before the first instruction of the run executes. The generator emits this instead of one
    /// <see cref="VerifySpeculativeEntryOrFail"/> per instruction when it proves nothing between the run
    /// entry and a later instruction can change the run's bytes or leave the run. The failure message
    /// names the first instruction whose bytes differ, so a mid-run mutation is reported at the
    /// instruction that actually changed.
    /// </summary>
    /// <param name="segment">The segment of the run's first instruction.</param>
    /// <param name="offset">The offset of the run's first instruction.</param>
    /// <param name="runSignatures">The signature of each instruction of the run, in memory order;
    /// each signature's length is its instruction's length in bytes.</param>
    /// <exception cref="UnrecoverableException">Thrown when memory at any instruction of the run does not match its expected signature.</exception>
    public void VerifySpeculativeRunOrFail(ushort segment, ushort offset, byte?[][] runSignatures) {
        foreach (byte?[] signature in runSignatures) {
            VerifySpeculativeEntryOrFail(segment, offset, signature);
            offset = (ushort)(offset + signature.Length);
        }
    }

    /// <summary>
    /// Throws an exception if the given value is not one of the possible values.
    /// </summary>
    /// <param name="value">The value to check for support.</param>
    /// <param name="possibleValues">The list of possible values that are supported.</param>
    /// <exception cref="UnrecoverableException">Thrown if the value is not in the list of supported values.</exception>
    public void FailIfValueIsNot(uint value, params uint[] possibleValues) {
        if (!possibleValues.Contains(value)) {
            throw FailAsUntested($"Value {value} not in list of supported values");
        }
    }

    /// <summary>
    /// Runs any pending interrupt requests. <br/>
    /// As long as the target programs triggers interrupts, you must call this often enough so interrupts can run.
    /// </summary>
    /// <param name="expectedReturnCs">The excepted value of the CS register after the interruption is done.</param>
    /// <param name="expectedReturnIp">The excepted value of the IP register after the interruption is done</param>
    public void CheckExternalEvents(ushort expectedReturnCs, ushort expectedReturnIp) {
        if (!State.IsRunning) {
            Exit();
        }
        State.IncCycles();
        // Override/generated code runs without returning to the interpreter run loop that pumps each of
        // these between instructions, so mirror that order here: increment the cycle, fire cycle
        // breakpoints (which may enqueue input events), process pending input, advance scheduled device
        // events (e.g. the PIT raising IRQ0), then deliver a pending PIC interrupt. Without this, queued
        // UI/headless input and external hardware interrupts would never reach generated code.
        Machine.EmulatorBreakpointsManager.CheckCpuCycleBreakPoints();
        Machine.InputEventHub.ProcessAllPendingInputEvents();
        Machine.EmulationLoopScheduler.ProcessEvents();
        if (!InterruptFlag) {
            return;
        }
        byte? vectorNumber = _dualPic.ComputeVectorNumber();
        if (vectorNumber != null) {
            InterruptCall(expectedReturnCs, expectedReturnIp, vectorNumber.Value);
        }
    }

    /// <summary>
    /// Call this to perform an interrupt request.
    /// </summary>
    /// <param name="vectorNumber">The vector number to call</param>
    public void Interrupt(byte vectorNumber) {
        _callbackHandler.RunFromOverriden(vectorNumber);
    }

    /// <summary>
    /// Defines C# functions for provided interrupt handlers so that when overriden code generates an interrupt, an override for it is found and executed.
    /// Does not currently handle mouse code which has more than a callback + iret.
    /// </summary>
    public void SetProvidedInterruptHandlersAsOverridden() {
        InterruptVectorTable ivt = new InterruptVectorTable(Memory);
        for (byte i = 0; i < 0xFF; i++) {
            SegmentedAddress handlerAddress = ivt[i];
            if (handlerAddress.Segment == 0 && handlerAddress.Offset == 0) {
                continue;
            }
            int callback = i;
            DefineFunction(handlerAddress.Segment, handlerAddress.Offset, (offset) => {
                _callbackHandler.RunFromOverriden(callback);

                return InterruptRet();
            }, false, $"provided_interrupt_handler_{ConvertUtils.ToHex(i)}");
        }
    }

    /// <summary>
    /// Performs unsigned 8-bit division.
    /// </summary>
    /// <param name="dividend">The 16-bit dividend (AX).</param>
    /// <param name="divisor">The 8-bit divisor.</param>
    /// <returns>A tuple containing the 8-bit quotient (AL) and 8-bit remainder (AH) as unsigned register bits.</returns>
    /// <exception cref="CpuDivisionErrorException">Thrown when the divisor is zero or the quotient does not fit in a byte.</exception>
    public (byte Quotient, byte Remainder) Div8(ushort dividend, byte divisor) {
        byte quotient = Alu8.Div(dividend, divisor);
        return (quotient, (byte)(dividend % divisor));
    }

    /// <summary>
    /// Performs unsigned 16-bit division.
    /// </summary>
    /// <param name="dividend">The 32-bit dividend (DX:AX).</param>
    /// <param name="divisor">The 16-bit divisor.</param>
    /// <returns>A tuple containing the 16-bit quotient (AX) and 16-bit remainder (DX) as unsigned register bits.</returns>
    /// <exception cref="CpuDivisionErrorException">Thrown when the divisor is zero or the quotient does not fit in a word.</exception>
    public (ushort Quotient, ushort Remainder) Div16(uint dividend, ushort divisor) {
        ushort quotient = Alu16.Div(dividend, divisor);
        return (quotient, (ushort)(dividend % divisor));
    }

    /// <summary>
    /// Performs unsigned 32-bit division.
    /// </summary>
    /// <param name="dividend">The 64-bit dividend (EDX:EAX).</param>
    /// <param name="divisor">The 32-bit divisor.</param>
    /// <returns>A tuple containing the 32-bit quotient (EAX) and 32-bit remainder (EDX) as unsigned register bits.</returns>
    /// <exception cref="CpuDivisionErrorException">Thrown when the divisor is zero or the quotient does not fit in a dword.</exception>
    public (uint Quotient, uint Remainder) Div32(ulong dividend, uint divisor) {
        uint quotient = Alu32.Div(dividend, divisor);
        return (quotient, (uint)(dividend % divisor));
    }

    /// <summary>
    /// Performs signed 8-bit division.
    /// </summary>
    /// <param name="dividend">The 16-bit signed dividend (AX).</param>
    /// <param name="divisor">The 8-bit signed divisor.</param>
    /// <returns>A tuple containing the 8-bit quotient (AL) and 8-bit remainder (AH) as unsigned register bits.</returns>
    /// <exception cref="CpuDivisionErrorException">Thrown when the divisor is zero or the quotient does not fit in a signed byte.</exception>
    public (byte Quotient, byte Remainder) IDiv8(short dividend, sbyte divisor) {
        sbyte quotient = Alu8.Idiv(dividend, divisor);
        return (unchecked((byte)quotient), unchecked((byte)(dividend % divisor)));
    }

    /// <summary>
    /// Performs signed 16-bit division.
    /// </summary>
    /// <param name="dividend">The 32-bit signed dividend (DX:AX).</param>
    /// <param name="divisor">The 16-bit signed divisor.</param>
    /// <returns>A tuple containing the 16-bit quotient (AX) and 16-bit remainder (DX) as unsigned register bits.</returns>
    /// <exception cref="CpuDivisionErrorException">Thrown when the divisor is zero or the quotient does not fit in a signed word.</exception>
    /// <exception cref="OverflowException">Thrown when the dividend is <see cref="int.MinValue"/> and the divisor is -1 (inherited ALU behavior).</exception>
    public (ushort Quotient, ushort Remainder) IDiv16(int dividend, short divisor) {
        short quotient = Alu16.Idiv(dividend, divisor);
        return (unchecked((ushort)quotient), unchecked((ushort)(dividend % divisor)));
    }

    /// <summary>
    /// Performs signed 32-bit division.
    /// </summary>
    /// <param name="dividend">The 64-bit signed dividend (EDX:EAX).</param>
    /// <param name="divisor">The 32-bit signed divisor.</param>
    /// <returns>A tuple containing the 32-bit quotient (EAX) and 32-bit remainder (EDX) as unsigned register bits.</returns>
    /// <exception cref="CpuDivisionErrorException">Thrown when the divisor is zero or the quotient does not fit in a signed dword.</exception>
    /// <exception cref="OverflowException">Thrown when the dividend is <see cref="long.MinValue"/> and the divisor is -1 (inherited ALU behavior).</exception>
    public (uint Quotient, uint Remainder) IDiv32(long dividend, int divisor) {
        int quotient = Alu32.Idiv(dividend, divisor);
        return (unchecked((uint)quotient), unchecked((uint)(dividend % divisor)));
    }

    /// <summary>
    /// Performs unsigned 16-bit multiplication.
    /// </summary>
    /// <param name="value1">The 16-bit multiplicand (AX).</param>
    /// <param name="value2">The 16-bit multiplier.</param>
    /// <returns>A tuple containing the 16-bit low half (AX) and 16-bit high half (DX) of the 32-bit product.</returns>
    public (ushort Low, ushort High) Mul16(ushort value1, ushort value2) {
        uint result = Alu16.Mul(value1, value2);
        return ((ushort)result, (ushort)(result >> 16));
    }

    /// <summary>
    /// Performs unsigned 32-bit multiplication.
    /// </summary>
    /// <param name="value1">The 32-bit multiplicand (EAX).</param>
    /// <param name="value2">The 32-bit multiplier.</param>
    /// <returns>A tuple containing the 32-bit low half (EAX) and 32-bit high half (EDX) of the 64-bit product.</returns>
    public (uint Low, uint High) Mul32(uint value1, uint value2) {
        ulong result = Alu32.Mul(value1, value2);
        return ((uint)result, (uint)(result >> 32));
    }

    /// <summary>
    /// Performs signed 16-bit multiplication.
    /// </summary>
    /// <param name="value1">The 16-bit signed multiplicand (AX).</param>
    /// <param name="value2">The 16-bit signed multiplier.</param>
    /// <returns>A tuple containing the 16-bit low half (AX) and 16-bit high half (DX) of the 32-bit product as unsigned register bits.</returns>
    public (ushort Low, ushort High) IMul16(short value1, short value2) {
        int result = Alu16.Imul(value1, value2);
        return (unchecked((ushort)result), unchecked((ushort)(result >> 16)));
    }

    /// <summary>
    /// Performs signed 32-bit multiplication.
    /// </summary>
    /// <param name="value1">The 32-bit signed multiplicand (EAX).</param>
    /// <param name="value2">The 32-bit signed multiplier.</param>
    /// <returns>A tuple containing the 32-bit low half (EAX) and 32-bit high half (EDX) of the 64-bit product as unsigned register bits.</returns>
    public (uint Low, uint High) IMul32(int value1, int value2) {
        long result = Alu32.Imul(value1, value2);
        return (unchecked((uint)result), unchecked((uint)(result >> 32)));
    }

    /// <summary>
    /// Reads a byte from an I/O port, like the <c>in</c> instruction.
    /// </summary>
    /// <param name="port">The I/O port to read from.</param>
    /// <returns>The byte read from the port.</returns>
    public byte In8(ushort port) => Machine.IoPortDispatcher.ReadByte(port);

    /// <summary>
    /// Reads a word from an I/O port, like the <c>in</c> instruction.
    /// </summary>
    /// <param name="port">The I/O port to read from.</param>
    /// <returns>The word read from the port.</returns>
    public ushort In16(ushort port) => Machine.IoPortDispatcher.ReadWord(port);

    /// <summary>
    /// Reads a double word from an I/O port, like the <c>in</c> instruction.
    /// </summary>
    /// <param name="port">The I/O port to read from.</param>
    /// <returns>The double word read from the port.</returns>
    public uint In32(ushort port) => Machine.IoPortDispatcher.ReadDWord(port);

    /// <summary>
    /// Writes a byte to an I/O port, like the <c>out</c> instruction.
    /// </summary>
    /// <param name="port">The I/O port to write to.</param>
    /// <param name="value">The byte to write.</param>
    public void Out8(ushort port, byte value) => Machine.IoPortDispatcher.WriteByte(port, value);

    /// <summary>
    /// Writes a word to an I/O port, like the <c>out</c> instruction.
    /// </summary>
    /// <param name="port">The I/O port to write to.</param>
    /// <param name="value">The word to write.</param>
    public void Out16(ushort port, ushort value) => Machine.IoPortDispatcher.WriteWord(port, value);

    /// <summary>
    /// Writes a double word to an I/O port, like the <c>out</c> instruction.
    /// </summary>
    /// <param name="port">The I/O port to write to.</param>
    /// <param name="value">The double word to write.</param>
    public void Out32(ushort port, uint value) => Machine.IoPortDispatcher.WriteDWord(port, value);

    /// <summary>
    /// Index of the lowest set bit of a word, like the <c>bsf</c> instruction.
    /// </summary>
    /// <param name="value">The value to scan.</param>
    /// <returns>The bit index (0-15) of the lowest set bit.</returns>
    public ushort BitScanForward16(ushort value) => (ushort)BitOperations.TrailingZeroCount(value);

    /// <summary>
    /// Index of the lowest set bit of a double word, like the <c>bsf</c> instruction.
    /// </summary>
    /// <param name="value">The value to scan.</param>
    /// <returns>The bit index (0-31) of the lowest set bit.</returns>
    public uint BitScanForward32(uint value) => (uint)BitOperations.TrailingZeroCount(value);

    /// <summary>
    /// Index of the highest set bit of a word, like the <c>bsr</c> instruction.
    /// </summary>
    /// <param name="value">The value to scan.</param>
    /// <returns>The bit index (0-15) of the highest set bit.</returns>
    public ushort BitScanReverse16(ushort value) => (ushort)BitOperations.Log2(value);

    /// <summary>
    /// Index of the highest set bit of a double word, like the <c>bsr</c> instruction.
    /// </summary>
    /// <param name="value">The value to scan.</param>
    /// <returns>The bit index (0-31) of the highest set bit.</returns>
    public uint BitScanReverse32(uint value) => (uint)BitOperations.Log2(value);

    /// <summary>
    /// Checks that a 16-bit signed index lies within [lower, upper], like the <c>bound</c> instruction.
    /// </summary>
    /// <exception cref="CpuBoundRangeExceededException">The index is outside the bounds.</exception>
    public void CheckBound(short index, short lower, short upper) {
        if (index < lower || index > upper) {
            throw new CpuBoundRangeExceededException(
                $"BOUND check failed: index={index}, lower={lower}, upper={upper}.");
        }
    }

    /// <summary>
    /// Checks that a 32-bit signed index lies within [lower, upper], like the <c>bound</c> instruction.
    /// </summary>
    /// <exception cref="CpuBoundRangeExceededException">The index is outside the bounds.</exception>
    public void CheckBound(int index, int lower, int upper) {
        if (index < lower || index > upper) {
            throw new CpuBoundRangeExceededException(
                $"BOUND check failed: index={index}, lower={lower}, upper={upper}.");
        }
    }

    /// <summary>
    /// Halt the program.
    /// </summary>
    /// <returns>An <see cref="Action"/> that exits the program.</returns>
    public Action Hlt() => Exit;

    /// <summary>
    /// Exit the program.
    /// </summary>
    /// <exception cref="HaltRequestedException">The exception throw in order to exit the program.</exception>
    protected void Exit() {
        _loggerService.LogTrace("Program requested exit. Terminating now");

        throw new HaltRequestedException();
    }

    /// <summary>C# code that called <c>NearCall</c>, <c>FarCall</c> or <c>InterruptCall</c> and waits for
    /// the chain to come back to the address that call expects to return to.</summary>
    private sealed class CSharpCallChainCaller : IOverrideChainCaller {
        private readonly CSharpOverrideHelper _helper;
        private readonly SegmentedAddress _expectedReturn;
        private readonly uint _expectedStackAddress;
        private readonly SegmentedAddress[] _redirectedReturns;

        /// <summary>Creates the caller waiting for a call that expects to return to
        /// <paramref name="expectedReturn"/>, started with the stack at <paramref name="expectedStackAddress"/>.</summary>
        /// <param name="helper">Helper that executed the call.</param>
        /// <param name="expectedReturn">Address the call expects to return to.</param>
        /// <param name="expectedStackAddress">Stack address before the call.</param>
        /// <param name="redirectedReturns">Other addresses the call accepts as its return.</param>
        public CSharpCallChainCaller(CSharpOverrideHelper helper, SegmentedAddress expectedReturn, uint expectedStackAddress, SegmentedAddress[] redirectedReturns) {
            _helper = helper;
            _expectedReturn = expectedReturn;
            _expectedStackAddress = expectedStackAddress;
            _redirectedReturns = redirectedReturns;
        }

        /// <summary>True when the chain ended at a redirected return address instead of the expected one.</summary>
        public bool EndedAtRedirectedReturn { get; private set; }

        /// <summary>A landed address is an accepted redirected return of a call when it is listed in
        /// <paramref name="redirectedReturns"/> and the stack is back at or above its level before the
        /// call's pushes (at or above so <c>ret n</c>, which pops more than the frame, also counts).
        /// The runtime check is looser than discovery: discovery requires an exact stack match to
        /// record a redirected return, but generated code reads the stack pointer from the CPU state
        /// at landing time, so continuing at a listed address with a higher stack pointer does what
        /// the interpreter does. The address itself is always one discovery observed with an exact
        /// stack match.</summary>
        public static bool IsRedirectedReturn(SegmentedAddress[] redirectedReturns, SegmentedAddress landedAddress,
            uint stackPhysicalAddress, uint expectedStackAddress) =>
            stackPhysicalAddress >= expectedStackAddress && redirectedReturns.Contains(landedAddress);

        /// <inheritdoc />
        public SegmentedAddress? ExpectedReturnAddress => _expectedReturn;

        /// <inheritdoc />
        public bool ContinuesAtExpectedReturn => true;

        /// <inheritdoc />
        public bool AcceptsRedirectedReturn(SegmentedAddress landedAddress, uint stackPhysicalAddress) {
            bool accepted = IsRedirectedReturn(_redirectedReturns, landedAddress, stackPhysicalAddress, _expectedStackAddress);
            EndedAtRedirectedReturn |= accepted;
            return accepted;
        }

        /// <inheritdoc />
        public bool RunOverride(FunctionInformation function, Func<int, Action> functionOverride) {
            SegmentedAddress actualReturn = _helper.State.IpSegmentedAddress;
            uint actualStackAddress = _helper.State.StackPhysicalAddress;
            if (actualStackAddress < _expectedStackAddress) {
                if (_helper._loggerService.IsEnabled(LogLevel.Debug)) {
                    _helper._loggerService.LogDebug(
                        "The call expecting to return to {ExpectedReturn} continued at {ActualReturn} while the callee's frame was still on the stack (stack {ActualStackAddress}, before the call {ExpectedStackAddress}). Running {FunctionName} inside the call.",
                        _expectedReturn, actualReturn, actualStackAddress, _expectedStackAddress, function.Name);
                }
            } else {
                _helper.LogNestedOverrideCall(_expectedReturn, actualReturn, _expectedStackAddress,
                    actualStackAddress, function.Name);
            }

            // A consumed unwind leaves CS:IP at an accepted return address, so the next step of the chain ends it.
            _helper.ExecuteCallConsumingUnwindTo(functionOverride,
                () => functionOverride.Invoke(0).Invoke(), _expectedReturn, _expectedStackAddress, _redirectedReturns);
            return false;
        }

        /// <inheritdoc />
        public void OnMachineStopped() {
            _helper.Exit();
        }
    }
}
