namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.Diagnostics.CodeAnalysis;

using static System.Runtime.CompilerServices.RuntimeHelpers;

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
        AddAction(0x00, GetKeystroke);
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

        //If games that use those unsupported interrupts misbehave or crash, check if this state is the proper way to
        //fix it as I couldn't find documentation about it
        State.CarryFlag = true;
        State.AX = 0;
    }

    /// <inheritdoc/>
    public override byte VectorNumber => 0x16;

    public override SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter) {
        return base.WriteAssemblyInRam(memoryAsmWriter);
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