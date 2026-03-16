namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.IOPorts;
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
    private readonly IOPortDispatcher _ioPortDispatcher;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="ioPortDispatcher">The I/O port dispatcher.</param>
    /// <param name="biosDataArea">The BIOS data structure holding state information.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="biosKeyboardBuffer">The FIFO queue used to store keyboard keys for the BIOS.</param>
    public KeyboardInt16Handler(IMemory memory, IOPortDispatcher ioPortDispatcher, BiosDataArea biosDataArea,
        IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state,
        ILoggerService loggerService, BiosKeyboardBuffer biosKeyboardBuffer)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _biosDataArea = biosDataArea;
        _ioPortDispatcher = ioPortDispatcher;
        _biosKeyboardBuffer = biosKeyboardBuffer;
        FillDispatchTable();
    }

    private void FillDispatchTable() {
        AddAction(0x01, () => GetKeystrokeStatus(true));
        AddAction(0x02, GetShiftFlags);
        AddAction(0x03, SetTypematicRateAndDelay);
        AddAction(0x1D, () => Unsupported(0x1D));
    }

    /// <summary>
    /// Sets the typematic rate and delay for keyboard repeat.
    /// <para>AH = 03h, AL = 05h</para>
    /// <para>BL = Typematic rate (bits 5-7 must be 0):</para>
    /// <code>
    /// ; REGISTER	   RATE      REGISTER	  RATE
    /// ;   VALUE	 SELECTED     VALUE	    SELECTED
    /// ;-----------------------------------------------
    /// ;00H	   	   30.0       10H	   	   7.5
    /// ;01H	   	   26.7       11H	   	   6.7
    /// ;02H	   	   24.0       12H	   	   6.0
    /// ;03H	   	   21.8       13H	   	   5.5
    /// ;04H	   	   20.0       14H	   	   5.0
    /// ;05H	   	   18.5       15H	   	   4.6
    /// ;06H	   	   17.1       16H	   	   4.3
    /// ;07H	   	   16.0       17H	   	   4.0
    /// ;08H	   	   15.0       18H	   	   3.7
    /// ;09H	   	   13.3       19H	   	   3.3
    /// ;0AH	   	   12.0       1AH	   	   3.0
    /// ;0BH	   	   10.9       1BH	   	   2.7
    /// ;0CH	   	   10.0       1CH	   	   2.5
    /// ;0DH	   	   9.2        1DH	   	   2.3
    /// ;0EH	   	   8.6        1EH	   	   2.1
    /// ;0FH	   	   8.0        1FH	   	   2.0
    /// </code>
    /// <para>BH = Typematic delay (bits 2-7 must be 0):</para>
    /// <list type="table">
    /// <listheader><term>Value</term><description>Delay</description></listheader>
    /// <item><term>00h</term><description>250ms</description></item>
    /// <item><term>01h</term><description>500ms</description></item>
    /// <item><term>02h</term><description>750ms</description></item>
    /// <item><term>03h</term><description>1000ms</description></item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// source: <a href="https://github.com/gawlas/IBM-PC-BIOS" /> <br/>
    /// Used by (for example): <a href="https://www.the8bitguy.com/product/planet-x3-for-ms-dos-computers/">Planet X3</a>
    /// </remarks>
    public void SetTypematicRateAndDelay() {
        switch (State.AL) {
            case TypematicFunctionConsts.SetDefaultFunction:
                _ioPortDispatcher.WriteByte(KeyboardPorts.Data, (byte)KeyboardCommand.SetTypeRate);
                _ioPortDispatcher.WriteByte(KeyboardPorts.Data, TypematicFunctionConsts.DefaultRateAndDelayValue);
                break;
            case TypematicFunctionConsts.SetRateAndDelayFunction:
                if ((State.BL & TypematicFunctionConsts.RateMask) == 0 && (State.BH & TypematicFunctionConsts.DelayMask) == 0) {
                    _ioPortDispatcher.WriteByte(KeyboardPorts.Data, (byte)KeyboardCommand.SetTypeRate);
                    byte rateAndDelay = (byte)(State.BH << TypematicFunctionConsts.DelayShiftCount | State.BL);
                    _ioPortDispatcher.WriteByte(KeyboardPorts.Data, rateAndDelay);
                }
                break;
            default:
                if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
                    LoggerService.Warning(
                        "{ClassName} INT {Int:X2} {operation}: Unhandled {MethodName} command.",
                        nameof(KeyboardInt16Handler), VectorNumber, State.AL, nameof(SetTypematicRateAndDelay));
                }
                break;
        }
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
    /// <remarks>Returns <c>0</c> if no key is available. Should not happen for emulated programs, see ASM above.</remarks>
    public void GetKeystroke() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("READ KEY STROKE");
        }
        if (TryGetPendingKeyCode(out ushort? keyCode)) {
            _biosKeyboardBuffer.DequeueKeyCode();
            State.AX = keyCode.Value;
        } else {
            State.AX = 0; // No key available
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

    /// <summary>
    /// Constants values used for Keyboard Typematic Rate and Delay operations (INT 16h AH=03h).
    /// </summary>
    public static class TypematicFunctionConsts {
        /// <summary>
        /// Function code to set default typematic rate and delay (AL=00h).
        /// </summary>
        public const byte SetDefaultFunction = 0x00;

        /// <summary>
        /// Function code to set custom typematic rate and delay (AL=05h).
        /// </summary>
        public const byte SetRateAndDelayFunction = 0x05;

        /// <summary>
        /// Bit shift count to position the delay value (5 bits).
        /// </summary>
        public const byte DelayShiftCount = 0x05;

        /// <summary>
        /// Default typematic value combining 500ms delay and 30 chars/sec rate (0x20).
        /// </summary>
        public const byte DefaultRateAndDelayValue = 0x20;

        /// <summary>
        /// Mask to validate that reserved bits 5-7 in the rate value are zero.
        /// </summary>
        public const byte RateMask = 0xE0;

        /// <summary>
        /// Mask to validate that reserved bits 2-7 in the delay value are zero.
        /// </summary>
        public const byte DelayMask = 0xFC;
    }

}