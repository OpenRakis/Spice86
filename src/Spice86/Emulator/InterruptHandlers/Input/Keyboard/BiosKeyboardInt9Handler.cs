namespace Spice86.Emulator.InterruptHandlers.Input.Keyboard;

using Spice86.Emulator.Devices.Input.Keyboard;
using Spice86.Emulator.VM;
using Spice86.UI;
using Spice86.UI.Interfaces;

/// <summary>
/// Crude implementation of Int9
/// </summary>
public class BiosKeyboardInt9Handler : InterruptHandler {
    private readonly BiosKeyboardBuffer _biosKeyboardBuffer;
    private readonly Keyboard _keyboard;
    private readonly IKeyScanCodeConverter _keyScanCodeConverter;

    public BiosKeyboardInt9Handler(Machine machine, IKeyScanCodeConverter keyScanCodeConverter) : base(machine) {
        this._keyboard = machine.Keyboard;
        _keyScanCodeConverter = keyScanCodeConverter;
        this._biosKeyboardBuffer = new BiosKeyboardBuffer(machine.Memory);
        _biosKeyboardBuffer.Init();
    }

    public BiosKeyboardBuffer BiosKeyboardBuffer => _biosKeyboardBuffer;

    public override byte Index => 0x9;

    public override void Run() {
        byte? scancode = _keyboard.GetScanCode();
        if (scancode == null) {
            return;
        }

        byte? ascii = _keyScanCodeConverter.GetAsciiCode(scancode.Value);
        if (ascii == null) {
            ascii = 0;
        }

        _biosKeyboardBuffer.AddKeyCode((ushort)((scancode.Value << 8) | ascii.Value));
    }
}