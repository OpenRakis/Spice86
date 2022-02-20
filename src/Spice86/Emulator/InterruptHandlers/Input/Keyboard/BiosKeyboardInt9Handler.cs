namespace Spice86.Emulator.InterruptHandlers.Input.Keyboard;

using Spice86.Emulator.Devices.Input.Keyboard;
using Spice86.Emulator.VM;

/// <summary>
/// Crude implementation of Int9
/// </summary>
public class BiosKeyboardInt9Handler : InterruptHandler {
    private readonly BiosKeyboardBuffer _biosKeyboardBuffer;
    private readonly Keyboard _keyboard;

    public BiosKeyboardInt9Handler(Machine machine) : base(machine) {
        this._keyboard = machine.Keyboard;
        this._biosKeyboardBuffer = new BiosKeyboardBuffer(machine.Memory);
        _biosKeyboardBuffer.Init();
    }

    public BiosKeyboardBuffer GetBiosKeyboardBuffer() {
        return _biosKeyboardBuffer;
    }

    public override byte Index => 0x9;

    public override void Run() {
        byte? scancode = _keyboard.GetScancode();
        if (scancode == null) {
            return;
        }

        byte? ascii = KeyScancodeConverter.GetAsciiCode(scancode.Value);
        if (ascii == null) {
            ascii = 0;
        }

        _biosKeyboardBuffer.AddKeyCode((ushort)((scancode.Value << 8) | ascii.Value));
    }
}