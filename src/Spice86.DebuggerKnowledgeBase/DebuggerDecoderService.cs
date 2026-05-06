namespace Spice86.DebuggerKnowledgeBase;

using System.Diagnostics.CodeAnalysis;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Instructions;
using Spice86.DebuggerKnowledgeBase.Registries;
using Spice86.DebuggerKnowledgeBase.Xms;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Facade that the debugger UI (disassembly view, CFG view, IO/interrupt info pages) uses to
/// produce high-level <see cref="DecodedCall"/> values for what the program is doing.
/// </summary>
/// <remarks>
/// This service is purely read-only: every method reads the current live CPU and memory
/// state at call time (no snapshot or clone is taken) and returns a decoded view. It never
/// mutates emulator state. Because it observes live state directly, callers must ensure the
/// emulator is paused (or otherwise quiesced) for the duration of the call so that registers
/// and memory cannot change underneath the decoders. In practice, the debugger UI invokes
/// this service from the UI thread only while the emulator is paused.
/// </remarks>
public sealed class DebuggerDecoderService {
    /// <summary>
    /// Prefix used for function names installed by the emulator itself (interrupt handlers,
    /// the mouse driver, and any other "trampoline" produced via <c>MemoryAsmWriter</c>).
    /// </summary>
    public const string EmulatorProvidedFunctionNamePrefix = "provided_";

    private readonly InterruptDecoderRegistry _interruptDecoders;
    private readonly IoPortDecoderRegistry _ioPortDecoders;
    private readonly AsmRoutineDecoderRegistry _asmRoutineDecoders;
    private readonly FunctionCatalogue _functionCatalogue;
    private readonly XmsCallDecoder _xmsCallDecoder;
    private readonly State _state;
    private readonly IMemory _memory;
    private readonly IOPortDispatcher _ioPortDispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="DebuggerDecoderService"/>.
    /// </summary>
    /// <param name="interruptDecoders">Registry of interrupt decoders.</param>
    /// <param name="ioPortDecoders">Registry of I/O port decoders.</param>
    /// <param name="asmRoutineDecoders">Registry of ASM routine decoders.</param>
    /// <param name="functionCatalogue">Catalogue of all known functions, used to identify emulator-installed routines by name prefix.</param>
    /// <param name="state">Live CPU state, read at call time on each decode.</param>
    /// <param name="memory">Emulated memory bus.</param>
    /// <param name="ioPortDispatcher">I/O port dispatcher (used to read last port access).</param>
    public DebuggerDecoderService(
        InterruptDecoderRegistry interruptDecoders,
        IoPortDecoderRegistry ioPortDecoders,
        AsmRoutineDecoderRegistry asmRoutineDecoders,
        FunctionCatalogue functionCatalogue,
        State state,
        IMemory memory,
        IOPortDispatcher ioPortDispatcher) {
        _interruptDecoders = interruptDecoders;
        _ioPortDecoders = ioPortDecoders;
        _asmRoutineDecoders = asmRoutineDecoders;
        _functionCatalogue = functionCatalogue;
        _xmsCallDecoder = XmsDecoderRegistration.CreateDecoder();
        _state = state;
        _memory = memory;
        _ioPortDispatcher = ioPortDispatcher;
    }

    /// <summary>
    /// Tries to decode a software interrupt invocation in the current CPU state.
    /// </summary>
    /// <param name="vector">Interrupt vector number.</param>
    /// <param name="call">Decoded call when the method returns true; null otherwise.</param>
    public bool TryDecodeInterrupt(byte vector, [NotNullWhen(true)] out DecodedCall? call) {
        return _interruptDecoders.TryDecode(vector, _state, _memory, out call);
    }

    /// <summary>
    /// Tries to decode an I/O port read.
    /// </summary>
    /// <param name="port">I/O port number.</param>
    /// <param name="value">Value that was read.</param>
    /// <param name="width">Access width in bytes.</param>
    /// <param name="call">Decoded call when the method returns true; null otherwise.</param>
    public bool TryDecodeIoRead(ushort port, uint value, int width, [NotNullWhen(true)] out DecodedCall? call) {
        return _ioPortDecoders.TryDecodeRead(port, value, width, out call);
    }

    /// <summary>
    /// Tries to decode an I/O port write.
    /// </summary>
    /// <param name="port">I/O port number.</param>
    /// <param name="value">Value that was written.</param>
    /// <param name="width">Access width in bytes.</param>
    /// <param name="call">Decoded call when the method returns true; null otherwise.</param>
    public bool TryDecodeIoWrite(ushort port, uint value, int width, [NotNullWhen(true)] out DecodedCall? call) {
        return _ioPortDecoders.TryDecodeWrite(port, value, width, out call);
    }

    /// <summary>
    /// Tries to decode the entry point of an emulator-installed ASM routine.
    /// </summary>
    /// <param name="entryPoint">Address to test.</param>
    /// <param name="call">Decoded call when the method returns true; null otherwise.</param>
    public bool TryDecodeAsmRoutine(SegmentedAddress entryPoint, [NotNullWhen(true)] out DecodedCall? call) {
        return _asmRoutineDecoders.TryDecode(entryPoint, out call);
    }

    /// <summary>
    /// Returns true when the given address is the entry point of a function the emulator
    /// itself installed (its name starts with <see cref="EmulatorProvidedFunctionNamePrefix"/>).
    /// </summary>
    /// <param name="address">Address to test.</param>
    public bool IsEmulatorProvidedEntryPoint(SegmentedAddress address) {
        return TryGetEmulatorProvidedFunction(address, out _);
    }

    /// <summary>
    /// Returns the <see cref="FunctionInformation"/> registered at <paramref name="address"/> when
    /// it corresponds to a function installed by the emulator itself, identified by the
    /// <see cref="EmulatorProvidedFunctionNamePrefix"/> name prefix.
    /// </summary>
    /// <param name="address">Address to test.</param>
    /// <param name="info">Function information when the method returns true; null otherwise.</param>
    public bool TryGetEmulatorProvidedFunction(SegmentedAddress address, [NotNullWhen(true)] out FunctionInformation? info) {
        if (_functionCatalogue.FunctionInformations.TryGetValue(address, out FunctionInformation? candidate)
            && candidate.Name.StartsWith(EmulatorProvidedFunctionNamePrefix, System.StringComparison.Ordinal)) {
            info = candidate;
            return true;
        }
        info = null;
        return false;
    }

    /// <summary>
    /// Decodes the XMS call currently set up in the CPU state. XMS is invoked by far call to
    /// the address returned by INT 2Fh AX=4310h, so this entry point is not dispatched through
    /// the interrupt registry; the UI / debugger calls it directly when the program is at the
    /// XMS callback entry point.
    /// </summary>
    public DecodedCall DecodeXmsCall() {
        return _xmsCallDecoder.Decode(_state, _memory);
    }

    /// <summary>
    /// Returns the I/O port dispatcher's most recent read information, decoded if a decoder
    /// claims the port. Useful to enrich the IO/Interrupt info debug view.
    /// </summary>
    public ushort LastPortRead => _ioPortDispatcher.LastPortRead;

    /// <summary>
    /// Returns the last port that was written to.
    /// </summary>
    public ushort LastPortWritten => _ioPortDispatcher.LastPortWritten;

    /// <summary>
    /// Returns the value of the last port write.
    /// </summary>
    public uint LastPortWrittenValue => _ioPortDispatcher.LastPortWrittenValue;

    /// <summary>
    /// Returns a high-level reminder for the given mnemonic from the static 386
    /// instruction knowledge base, or null when the mnemonic is not yet covered.
    /// Lookup is case-insensitive and accepts common aliases (JE/JZ, RET/RETN, ...).
    /// </summary>
    /// <param name="mnemonic">Canonical mnemonic, e.g. "MOV", "JE", "CALL".</param>
    public InstructionInfo? GetInstructionInfo(string mnemonic) {
        Instruction386KnowledgeBase.TryGet(mnemonic, out InstructionInfo? info);
        return info;
    }

    /// <summary>
    /// Returns the list of interrupt vectors this knowledge base can decode, together
    /// with a short human-readable description, ordered by vector number.
    /// Useful for populating autocomplete suggestions in the breakpoint dialog.
    /// </summary>
    public IReadOnlyList<(byte Vector, string Description)> GetKnownInterrupts() {
        return KnownInterrupts;
    }

    /// <summary>
    /// Returns the list of I/O port ranges this knowledge base can decode, together
    /// with a short human-readable description, ordered by first port address.
    /// Useful for populating autocomplete suggestions in the breakpoint dialog.
    /// </summary>
    public IReadOnlyList<(ushort FirstPort, ushort LastPort, string Description)> GetKnownIoPorts() {
        return KnownIoPorts;
    }

    private static readonly IReadOnlyList<(byte Vector, string Description)> KnownInterrupts =
    [
        (0x08, "Timer Interrupt (IRQ 0 / PIT tick)"),
        (0x09, "Keyboard Interrupt (IRQ 1)"),
        (0x10, "BIOS Video Services (AH=function)"),
        (0x11, "BIOS Equipment List"),
        (0x12, "BIOS Conventional Memory Size"),
        (0x13, "BIOS Disk Services (AH=function)"),
        (0x15, "BIOS Miscellaneous Services / PS2 Pointing Device (AH=function)"),
        (0x16, "BIOS Keyboard Services (AH=function)"),
        (0x1A, "BIOS Time / Date Services (AH=function)"),
        (0x1C, "BIOS Timer Tick User Handler"),
        (0x20, "DOS Terminate Program"),
        (0x21, "DOS Functions (AH=function)"),
        (0x22, "DOS Terminate Address (control-flow target)"),
        (0x23, "DOS Ctrl-Break Handler"),
        (0x24, "DOS Critical Error Handler"),
        (0x25, "DOS Absolute Disk Read"),
        (0x26, "DOS Absolute Disk Write"),
        (0x28, "DOS Idle (background processing hook)"),
        (0x2A, "DOS Network / Critical Section"),
        (0x2F, "DOS Multiplex (incl. XMS driver address at AX=4310h)"),
        (0x33, "Mouse Driver Functions (AX=function)"),
        (0x67, "EMS Expanded Memory Services (AH=function)"),
        (0x70, "BIOS Real-Time Clock Interrupt (IRQ 8)"),
        (0x74, "BIOS PS/2 Mouse Interrupt (IRQ 12)")
    ];

    private static readonly IReadOnlyList<(ushort FirstPort, ushort LastPort, string Description)> KnownIoPorts =
    [
        (0x0020, 0x0021, "PIC1 — Master Programmable Interrupt Controller"),
        (0x0040, 0x0043, "PIT 8254 — Programmable Interval Timer"),
        (0x0060, 0x0060, "PS/2 Keyboard Data"),
        (0x0064, 0x0064, "PS/2 Keyboard / Mouse Controller Command & Status"),
        (0x00A0, 0x00A1, "PIC2 — Slave Programmable Interrupt Controller"),
        (0x0200, 0x0207, "Joystick Gameport (PC/AT standard)"),
        (0x0210, 0x021F, "Sound Blaster (base 0x210)"),
        (0x0220, 0x022F, "Sound Blaster (base 0x220 — most common)"),
        (0x0230, 0x023F, "Sound Blaster (base 0x230)"),
        (0x0240, 0x024F, "Sound Blaster (base 0x240) / Gravis Ultrasound"),
        (0x0250, 0x025F, "Sound Blaster (base 0x250)"),
        (0x0260, 0x026F, "Sound Blaster (base 0x260)"),
        (0x0280, 0x028F, "Sound Blaster (base 0x280)"),
        (0x0330, 0x0331, "MPU-401 MIDI Interface (General MIDI / MT-32)"),
        (0x0388, 0x038B, "OPL FM Synthesizer (AdLib / OPL2 / OPL3)"),
        (0x03B0, 0x03BF, "VGA Monochrome / MDA-compatible Registers"),
        (0x03C0, 0x03CF, "VGA Color / EGA-compatible Registers"),
        (0x03D0, 0x03DF, "VGA Color Additional / CGA-compatible Registers")
    ];
}
