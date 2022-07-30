namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Serilog;

using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.Callback;
using Spice86.Logging;

public class KeyboardInt16Handler : InterruptHandler {
    private static readonly ILogger _logger = new Serilogger().Logger.ForContext<KeyboardInt16Handler>();
    private readonly BiosKeyboardBuffer _biosKeyboardBuffer;

    public KeyboardInt16Handler(Machine machine, BiosKeyboardBuffer biosKeyboardBuffer) : base(machine) {
        _biosKeyboardBuffer = biosKeyboardBuffer;
        _dispatchTable.Add(0x00, new Callback(0x00, () => GetKeystroke()));
        _dispatchTable.Add(0x01, new Callback(0x01, () => GetKeystrokeStatus(true)));
    }

    public override byte Index => 0x16;

    public void GetKeystroke() {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("READ KEY STROKE");
        }
        ushort? keyCode = GetNextKeyCode();
        if (keyCode == null) {
            keyCode = 0;
        }

        // AH = keyboard scan code
        // AL = ASCII character or zero if special function key
        _state.AX = keyCode.Value;
    }

    public void GetKeystrokeStatus(bool calledFromVm) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("KEY STROKE STATUS");
        }

        // ZF = 0 if a key pressed (even Ctrl-Break)
        // AX = 0 if no scan code is available
        // AH = scan code
        // AL = ASCII character or zero if special function key
        if (_biosKeyboardBuffer.IsEmpty) {
            SetZeroFlag(true, calledFromVm);
            _state.AX = 0;
        } else {
            ushort? keyCode = _biosKeyboardBuffer.GetKeyCode();
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