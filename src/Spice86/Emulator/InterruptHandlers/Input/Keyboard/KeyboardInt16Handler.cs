namespace Spice86.Emulator.InterruptHandlers.Input.Keyboard;

using Serilog;

using Spice86.Emulator.Callback;
using Spice86.Emulator.Machine;

public class KeyboardInt16Handler : InterruptHandler {
    private static readonly ILogger _logger = Log.Logger.ForContext<KeyboardInt16Handler>();
    private BiosKeyboardBuffer biosKeyboardBuffer;

    public KeyboardInt16Handler(Machine machine, BiosKeyboardBuffer biosKeyboardBuffer) : base(machine) {
        this.biosKeyboardBuffer = biosKeyboardBuffer;
        _dispatchTable.Add(0x00, new Callback(0x00, () => this.GetKeystroke()));
        _dispatchTable.Add(0x01, new Callback(0x01, () => GetKeystrokeStatus(true)));
    }

    public override int GetIndex() {
        return 0x16;
    }

    public void GetKeystroke() {
        _logger.Information("READ KEY STROKE");
        int? keyCode = GetNextKeyCode();
        if (keyCode == null) {
            keyCode = 0;
        }

        // AH = keyboard scan code
        // AL = ASCII character or zero if special function key
        _state.SetAX(keyCode.Value);
    }

    public void GetKeystrokeStatus(bool calledFromVm) {
        _logger.Information("KEY STROKE STATUS");

        // ZF = 0 if a key pressed (even Ctrl-Break)
        // AX = 0 if no scan code is available
        // AH = scan code
        // AL = ASCII character or zero if special function key
        if (biosKeyboardBuffer.Empty()) {
            SetZeroFlag(true, calledFromVm);
            _state.SetAX(0);
        } else {
            int? keyCode = biosKeyboardBuffer.GetKeyCode();
            if (keyCode != null) {
                SetZeroFlag(false, calledFromVm);
                _state.SetAX(keyCode.Value);
            }
        }
    }

    public int? GetNextKeyCode() {
        return biosKeyboardBuffer.GetKeyCode();
    }

    public override void Run() {
        int operation = _state.GetAH();
        this.Run(operation);
    }
}