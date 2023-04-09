using Spice86.Logging;
using Spice86.Shared.Interfaces;

namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Serilog;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.VM;

public class KeyboardInt16Handler : InterruptHandler {
    private readonly ILoggerService _loggerService;
    private readonly BiosKeyboardBuffer _biosKeyboardBuffer;

    public KeyboardInt16Handler(Machine machine, ILoggerService loggerService, BiosKeyboardBuffer biosKeyboardBuffer) : base(machine) {
        _loggerService = loggerService;
        _biosKeyboardBuffer = biosKeyboardBuffer;
        _dispatchTable.Add(0x00, new Callback(0x00, () => GetKeystroke()));
        _dispatchTable.Add(0x01, new Callback(0x01, () => GetKeystrokeStatus(true)));
    }

    public override byte Index => 0x16;

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
            ushort? keyCode = _biosKeyboardBuffer.GetKeyCodeStatus();
            if (keyCode != null) {
                SetZeroFlag(false, calledFromVm);
                _state.AX = keyCode.Value;
            }
        }
    }

    public ushort? GetNextKeyCode() {
        return _biosKeyboardBuffer.GetKeyCode();
    }

    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }
}