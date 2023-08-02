
namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

public class KeyboardInt16Handler : InterruptHandler {
    private readonly BiosKeyboardBuffer _biosKeyboardBuffer;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="cpu">The emulated CPU.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="biosKeyboardBuffer">The FIFO queue used to store keyboard keys for the BIOS.</param>
    public KeyboardInt16Handler(IMemory memory, Cpu cpu, ILoggerService loggerService, BiosKeyboardBuffer biosKeyboardBuffer) : base(memory, cpu, loggerService) {
        _biosKeyboardBuffer = biosKeyboardBuffer;
        AddAction(0x00, () => GetKeystroke());
        AddAction(0x01, () => GetKeystrokeStatus(true));
    }

    public override byte VectorNumber => 0x16;

    public void GetKeystroke() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("READ KEY STROKE");
        }
        ushort? keyCode = GetNextKeyCode();
        keyCode ??= 0;

        // AH = keyboard scan code
        // AL = ASCII character or zero if special function key
        _state.AX = keyCode.Value;
    }

    public void GetKeystrokeStatus(bool calledFromVm) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("KEY STROKE STATUS");
        }

        // ZF = 0 if a key pressed (even Ctrl-Break)
        // AX = 0 if no scan code is available
        // AH = scan code
        // AL = ASCII character or zero if special function key
        if (_biosKeyboardBuffer.IsEmpty) {
            SetZeroFlag(true, calledFromVm);
            _state.AX = 0;
        } else {
            ushort? keyCode = _biosKeyboardBuffer.PeekKeyCode();
            if (keyCode != null) {
                SetZeroFlag(false, calledFromVm);
                _state.AX = keyCode.Value;
            }
        }
    }

    public ushort? GetNextKeyCode() {
        return _biosKeyboardBuffer.DequeueKeyCode();
    }

    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }
}