namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// BIOS keyboard services interrupt (INT 16h).
/// </summary>
public class KeyboardInt16Handler : InterruptHandler {
    private readonly BiosKeyboardBuffer _biosKeyboardBuffer;
    private readonly BiosDataArea _biosDataArea;

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
    public KeyboardInt16Handler(IMemory memory, BiosDataArea biosDataArea,
        IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state,
        ILoggerService loggerService, BiosKeyboardBuffer biosKeyboardBuffer)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
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
    /// Emits a minimal INT 16h handler stub into guest RAM.
    /// </summary>
    /// <remarks><![CDATA[
    /// Handles AH=00h (Get Keystroke) locally with a busy-wait loop driven by a callback that sets ZF,
    /// and dispatches all other AH values back to the C# handler.
    /// 
    /// Pseudo-assembly (labels for readability):
    /// 
    ///   L_HANDLER:
    ///     cmp ah, 00h
    ///     jz  L_AH00
    /// 
    ///   L_DEFAULT:
    ///     int cbDefaultDispatch   ; calls C# Run() to handle AH != 00h
    ///     iret
    /// 
    ///   L_AH00:
    ///   L_WAIT:
    ///     int cbGetKeystrokeStatus ; sets AX and ZF
    ///     jz  L_WAIT               ; ZF=1 => no key available, keep spinning with IF enabled
    ///     cli
    ///     int cbDequeueAndSetAx    ; AX = (scan<<8 | ascii), consumes the key
    ///     sti
    ///     iret
    /// 
    /// Notes:
    /// - Interrupts remain enabled during the wait loop so IRQ1 can populate the BIOS buffer.
    /// - A short JZ right after the CMP skips exactly the default-dispatch INT+IRET sequence.
    /// ]]></remarks>
    /// <param name="memoryAsmWriter">Helper used to write machine code and callbacks into guest memory.</param>
    /// <returns>The segment:offset address where the handler stub was emitted.</returns>
    public override SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter) {
        SegmentedAddress handlerAddress = memoryAsmWriter.CurrentAddress;

        // Ensure IF is set on entry (like PC XT BIOS does)
        memoryAsmWriter.WriteSti();

        // CMP AH, 00
        memoryAsmWriter.WriteUInt8(0x80);
        memoryAsmWriter.WriteUInt8(0xFC);
        memoryAsmWriter.WriteUInt8(0x00);
        // JZ L_AH00 (+3 to skip: INT <cb>, IRET)
        memoryAsmWriter.WriteJz(3);

        // L_DEFAULT: callback DefaultDispatch, then IRET
        memoryAsmWriter.RegisterAndWriteCallback(VectorNumber, Run);
        memoryAsmWriter.WriteIret();

        // L_AH00: Busy-wait using GetKeystrokeStatus callback (sets AX and ZF)
        SegmentedAddress ah00Address = memoryAsmWriter.CurrentAddress;

        // Call C# status: ZF=1 if empty (AX=0), ZF=0 and AX=key if pending (non-destructive)
        memoryAsmWriter.RegisterAndWriteCallback(CallbackGetKeystrokeStatus);

        // If key is available (ZF=0), skip the back-jump; else loop
        memoryAsmWriter.WriteJnz(2);

        // JMP short L_AH00
        sbyte offset = (sbyte)(ah00Address.Offset - (memoryAsmWriter.CurrentAddress.Offset + 2));
        memoryAsmWriter.WriteJumpShort(offset);

        // Key is available: dequeue atomically and return
        memoryAsmWriter.WriteCli();
        memoryAsmWriter.RegisterAndWriteCallback(CallbackDequeueAndSetAx);
        memoryAsmWriter.WriteSti();
        memoryAsmWriter.WriteIret();

        return handlerAddress;
    }

    /// <summary>
    /// Callback invoked by the ASM stub to query the keystroke status.
    /// Sets ZF and AX like BIOS INT 16h AH=01h (non-destructive read).
    /// </summary>
    private void CallbackGetKeystrokeStatus() {
        // Called from ASM callback
        GetKeystrokeStatus(false);
    }

    /// <summary>
    /// Callback invoked by the ASM stub to dequeue the pending key
    /// and place it in AX (AH=scan code, AL=ASCII or 0 for extended).
    /// </summary>
    private void CallbackDequeueAndSetAx() {
        if (!TryGetPendingKeyCode(out ushort? keyCode)) {
            // This should not happen if the ASM part is correct
            return;
        }

        _biosKeyboardBuffer.DequeueKeyCode();
        State.AX = keyCode.Value;
    }

    /// <summary>
    /// Returns the pending key code. For use by C# overrides of machine code.
    /// Not for the Spice86 emulator.
    /// </summary>
    /// <remarks>high byte is the scan code, low byte is the ASCII character code</remarks>
    public ushort GetKeystroke() {
        if (TryGetPendingKeyCode(out ushort? keyCode)) {
            _biosKeyboardBuffer.DequeueKeyCode();
            return keyCode.Value;
        } else {
            return 0;
        }
    }

    /// <summary>
    /// INT 16h AH=02h — Get Shift Flags.
    /// Returns the BIOS keyboard status flags in AL.
    /// </summary>
    public void GetShiftFlags() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("GET SHIFT FLAGS");
        }
        State.AL = _biosDataArea.KeyboardStatusFlag;
    }

    /// <summary>
    /// Tries to read the pending key code from the BIOS keyboard buffer without removing it.
    /// </summary>
    /// <param name="keyCode">On success, receives the key code; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> if a key is pending; otherwise, <c>false</c>.</returns>
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
    /// Returns whether the BIOS keyboard buffer contains a pending key code.
    /// </summary>
    public bool HasKeyCodePending() {
        return TryGetPendingKeyCode(out _);
    }

    /// <summary>
    /// INT 16h AH=01h — Check for Keystroke (non-destructive).
    /// Sets ZF=0 and returns AX=key if a keystroke is pending; otherwise sets ZF=1 and AX=0.
    /// </summary>
    /// <param name="calledFromVm">True if the method was called by internal emulator code; false otherwise.</param>
    public void GetKeystrokeStatus(bool calledFromVm) {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("KEY STROKE STATUS");
        }

        // ZF = 0 if a key is pending (even in the case of Ctrl-Break)
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
    /// Flushes the BIOS keyboard buffer, resetting head and tail to the start of the buffer.
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