namespace Spice86.DebuggerKnowledgeBase;

using System.Diagnostics.CodeAnalysis;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Common.RoutineInstall;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Registries;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Facade that the debugger UI (disassembly view, CFG view, IO/interrupt info pages) uses to
/// produce high-level <see cref="DecodedCall"/> values for what the program is doing.
/// </summary>
/// <remarks>
/// This service is purely read-only: every method takes a snapshot of CPU/memory state and
/// returns a decoded view. It never mutates emulator state. It is therefore safe to call from
/// the UI thread while the emulator is paused.
/// </remarks>
public sealed class DebuggerDecoderService {
    private readonly InterruptDecoderRegistry _interruptDecoders;
    private readonly IoPortDecoderRegistry _ioPortDecoders;
    private readonly AsmRoutineDecoderRegistry _asmRoutineDecoders;
    private readonly EmulatorProvidedCodeRegistry _emulatorProvidedCode;
    private readonly State _state;
    private readonly IMemory _memory;
    private readonly IOPortDispatcher _ioPortDispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="DebuggerDecoderService"/>.
    /// </summary>
    /// <param name="interruptDecoders">Registry of interrupt decoders.</param>
    /// <param name="ioPortDecoders">Registry of I/O port decoders.</param>
    /// <param name="asmRoutineDecoders">Registry of ASM routine decoders.</param>
    /// <param name="emulatorProvidedCode">Registry of emulator-installed routines.</param>
    /// <param name="state">Current CPU state (snapshotted on each call).</param>
    /// <param name="memory">Emulated memory bus.</param>
    /// <param name="ioPortDispatcher">I/O port dispatcher (used to read last port access).</param>
    public DebuggerDecoderService(
        InterruptDecoderRegistry interruptDecoders,
        IoPortDecoderRegistry ioPortDecoders,
        AsmRoutineDecoderRegistry asmRoutineDecoders,
        EmulatorProvidedCodeRegistry emulatorProvidedCode,
        State state,
        IMemory memory,
        IOPortDispatcher ioPortDispatcher) {
        _interruptDecoders = interruptDecoders;
        _ioPortDecoders = ioPortDecoders;
        _asmRoutineDecoders = asmRoutineDecoders;
        _emulatorProvidedCode = emulatorProvidedCode;
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
    /// Returns true when the given address falls inside an emulator-installed ASM routine.
    /// </summary>
    /// <param name="address">Address to test.</param>
    public bool IsEmulatorProvided(SegmentedAddress address) {
        return _emulatorProvidedCode.IsEmulatorProvided(address);
    }

    /// <summary>
    /// Returns metadata about the emulator-installed routine the given address belongs to, if any.
    /// </summary>
    /// <param name="address">Address to test.</param>
    /// <param name="info">Routine metadata when the method returns true; null otherwise.</param>
    public bool TryGetEmulatorProvidedRoutine(SegmentedAddress address, [NotNullWhen(true)] out ProvidedRoutineInfo? info) {
        return _emulatorProvidedCode.TryGet(address, out info);
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
}
