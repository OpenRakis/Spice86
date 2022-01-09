namespace Spice86.Emulator.InterruptHandlers.Input.Keyboard;

using Spice86.Emulator.Devices.Input.Keyboard;
using Spice86.Emulator.Machine;

/// <summary>
/// Crude implementation of Int9
/// </summary>
public class BiosKeyboardInt9Handler : InterruptHandler
{
    private KeyScancodeConverter _keyScancodeConverter = new();
    private BiosKeyboardBuffer _biosKeyboardBuffer;
    private Keyboard _keyboard;
    public BiosKeyboardInt9Handler(Machine machine) : base(machine)
    {
        this._keyboard = machine.GetKeyboard();
        this._biosKeyboardBuffer = new BiosKeyboardBuffer(machine.GetMemory());
        _biosKeyboardBuffer.Init();
    }

    public virtual BiosKeyboardBuffer GetBiosKeyboardBuffer()
    {
        return _biosKeyboardBuffer;
    }

    public override void Run()
    {
        int? scancode = _keyboard.GetScancode();
        if (scancode == null)
        {
            return;
        }

        int? ascii = _keyScancodeConverter.GetAsciiCode(scancode.Value);
        if (ascii == null)
        {
            ascii = 0;
        }

        _biosKeyboardBuffer.AddKeyCode((scancode.Value << 8) | ascii.Value);
    }

    public override int GetIndex()
    {
        return 0x9;
    }
}
