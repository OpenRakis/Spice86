namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// The keyboard controller interrupt (INT16H)
/// </summary>
public class KeyboardInt16Handler : InterruptHandler {
    private readonly BiosKeyboardBuffer _biosKeyboardBuffer;
    private readonly BiosDataArea _biosDataArea;
    private readonly EmulationLoopRecall _emulationLoopRecall;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="biosDataArea">The BIOS data structure holding state information.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="biosKeyboardBuffer">The FIFO queue used to store keyboard keys for the BIOS.</param>
    /// <param name="emulationLoopRecall">Class used to wait for keyboard input from hardware interrupt 0x9 (IRQ1)</param>
    public KeyboardInt16Handler(IMemory memory, BiosDataArea biosDataArea,
        IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state,
        ILoggerService loggerService, BiosKeyboardBuffer biosKeyboardBuffer,
        EmulationLoopRecall emulationLoopRecall)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _emulationLoopRecall = emulationLoopRecall;
        _biosDataArea = biosDataArea;
        _biosKeyboardBuffer = biosKeyboardBuffer;
        AddAction(0x01, () => GetKeystrokeStatus(true));
        AddAction(0x02, GetShiftFlags);
        AddAction(0x1D, () => Unsupported(0x1D));
    }

    private void Unsupported(int operation) {
        if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
            LoggerService.Warning(
                "{ClassName} INT {Int:X2} {operation}: Unhandled/undocumented keyboard interrupt called, will ignore",
                nameof(KeyboardInt16Handler), VectorNumber, operation);
        }

        //If games that use those unsupported interrupts misbehave or crash, check if certain flags/registers have to be set
        //properly, e.g., AX = 0 and/or setting the carry flag accordingly.
    }

    /// <inheritdoc/>
    public override byte VectorNumber => 0x16;

    /// <summary>
    ///     Emits a minimal INT 16h handler stub into guest RAM that handles AH=00h (GetKeystroke) in-place
    ///     and dispatches all other AH values back to the C# handler via a callback.
    /// </summary>
    /// <remarks><![CDATA[
    ///     Assembled control flow (labels for readability):
    ///     L_HANDLER:
    ///     cmp ah, 00h
    ///     jz  L_AH00
    /// 
    ///     L_DEFAULT:
    ///     int 0A4h   ; DefaultDispatch -> calls C# Run()
    ///     iret
    /// 
    ///     L_AH00:
    ///     int 0A0h   ; CallbackHasKey -> sets ZF=0 if a key is present
    ///     jnz have_key
    ///     int 09h    ; invoke hardware keyboard ISR (IRQ1) to fetch a key
    ///     jmp short L_AH00
    /// 
    ///     have_key:
    ///     int 0A1h   ; CallbackDequeueAndSetAx -> AX = (scan<<8 | ascii)
    ///     iret
    /// 
    ///     Callback vector mapping:
    ///     - 0xA0 => CallbackHasKey
    ///     - 0xA1 => CallbackDequeueAndSetAx
    ///     - 0xA4 => Run (default C# dispatcher for unsupported AH values)
    ///     Notes:
    ///     - The short jumps are encoded to skip over the DefaultDispatch+IRET and to loop back while waiting.
    ///     - This keeps the common AH=00h path fast in RAM while preserving behavior for other functions.
    /// ]]></remarks>
    /// <param name="memoryAsmWriter">Helper used to write machine code and callbacks into guest memory.</param>
    /// <returns>The segment:offset address where the handler stub was emitted.</returns>
    public override SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter) {
        // Only AH=00 (GetKeystroke) is implemented in the in-RAM handler.
        // All other AH values jump to the default C# dispatcher via callback.

        SegmentedAddress handlerAddress = memoryAsmWriter.CurrentAddress;

        // CMP AH, 00
        memoryAsmWriter.WriteUInt8(0x80);
        memoryAsmWriter.WriteUInt8(0xFC);
        memoryAsmWriter.WriteUInt8(0x00);
        // JZ L_AH00 (+0x05 to skip the default callback and IRET)
        memoryAsmWriter.WriteJz(4 + 1);

        // L_DEFAULT: callback DefaultDispatch then IRET
        memoryAsmWriter.RegisterAndWriteCallback(VectorNumber, Run);
        memoryAsmWriter.WriteIret();

        // L_AH00:
        // callback HasKey (sets ZF=0 when key present)
        memoryAsmWriter.RegisterAndWriteCallback(CallbackHasKey);
        // JNZ have_key (+0x04 to skip INT 09h and the backward jump)
        memoryAsmWriter.WriteJnz(4);
        // INT 09h
        memoryAsmWriter.WriteInt(0x09);
        // JMP short back to L_AH00 (-0x10)
        memoryAsmWriter.WriteJumpShort(-10);
        // have_key: dequeue and set AX, then IRET
        memoryAsmWriter.RegisterAndWriteCallback(CallbackDequeueAndSetAx);
        memoryAsmWriter.WriteIret();

        return handlerAddress;
    }

    // Callbacks used by the in-memory INT 16h stub
    private void CallbackHasKey() {
        // ZF = 1 when empty, 0 when a key is available
        SetZeroFlag(_biosKeyboardBuffer.IsEmpty, false);
    }

    private void CallbackDequeueAndSetAx() {
        if (!TryGetPendingKeyCode(out ushort? keyCode)) {
            return;
        }

        _biosKeyboardBuffer.DequeueKeyCode();
        State.AX = keyCode.Value;
    }

    /// <summary>
    /// Returns in the AX register the pending key code.
    /// </summary>
    /// <remarks>AH is the scan code, AL is the ASCII character code</remarks>
    public void GetKeystroke() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("READ KEY STROKE");
        }
        if (TryGetPendingKeyCode(out ushort? keyCode)) {
            _biosKeyboardBuffer.DequeueKeyCode();
            State.AX = keyCode.Value;
        } else {
            while (_biosKeyboardBuffer.IsEmpty) {
                //Wait for hardware interrupt 0x9 (IRQ1) to be processed
                _emulationLoopRecall.RunInterrupt(0x9);
            }
        }
    }

    public void GetShiftFlags() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("GET SHIFT FLAGS");
        }
        State.AL = _biosDataArea.KeyboardStatusFlag;
    }

    /// <summary>
    /// Tries to get the pending keycode from the BIOS keyboard buffer without removing it.
    /// </summary>
    /// <param name="keyCode">When this method returns, contains the keycode if one was available; otherwise, the default value.</param>
    /// <returns><c>True</c> if a keycode was available; otherwise, <c>False</c>.</returns>
    public bool TryGetPendingKeyCode([NotNullWhen(true)] out ushort? keyCode) {
        ushort? code = _biosKeyboardBuffer.PeekKeyCode();
        if (code.HasValue) {
            keyCode = code.Value;
            return true;
        }
        keyCode = null;
        return false;
    }

    /// <summary>
    /// Gets whether the BIOS keyboard buffer has a pending key code in its queue.
    /// </summary>
    /// <returns><c>True</c> if the BIOS keyboard buffer is not empty, <c>False</c> otherwise.</returns>
    public bool HasKeyCodePending() {
        return TryGetPendingKeyCode(out _);
    }

    /// <summary>
    /// Returns in the AX CPU register the pending key code without removing it from the BIOS keyboard buffer. Returns 0 in AX with the CPU Zero Flag set if there was nothing in the buffer.
    /// </summary>
    /// <param name="calledFromVm"><c>True</c> ff the method was called by internal emulator code, <c>False</c> otherwise.</param>
    public void GetKeystrokeStatus(bool calledFromVm) {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("KEY STROKE STATUS");
        }

        // ZF = 0 if a key pressed (even in the case of Ctrl-Break)
        // AX = 0 if no scan code is available
        // AH = scan code
        // AL = ASCII character or zero if special function key
        if (_biosKeyboardBuffer.IsEmpty) {
            SetZeroFlag(true, calledFromVm);
            State.AX = 0;
        } else {
            ushort? keyCode = _biosKeyboardBuffer.PeekKeyCode();
            if (keyCode != null) {
                SetZeroFlag(false, calledFromVm);
                State.AX = keyCode.Value;
            }
        }
    }

    /// <summary>
    /// Tells the BIOS keyboard buffer to flush its contents, setting the head and tail addresses to the start of the buffer.
    /// </summary>
    public void FlushKeyboardBuffer() {
        _biosKeyboardBuffer.Flush();
    }

    /// <inheritdoc/>
    public override void Run() {
        byte operation = State.AH;
        Run(operation);
    }
}