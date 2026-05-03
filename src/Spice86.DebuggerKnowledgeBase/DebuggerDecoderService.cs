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
}
