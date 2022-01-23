namespace Spice86.Emulator.Devices.Input.Keyboard;

using Avalonia.Input;

using Serilog;

using Spice86.Emulator.IOPorts;
using Spice86.Emulator.VM;
using Spice86.UI;

/// <summary>
/// Basic implementation of a keyboard
/// </summary>
public class Keyboard : DefaultIOPortHandler {
    private const int KeyboardIoPort = 0x60;
    private static readonly ILogger _logger = Log.Logger.ForContext<Keyboard>();
    private readonly KeyScancodeConverter _keyScancodeConverter = new();
    private readonly Gui? gui;

    public Keyboard(Machine machine, Gui? gui, bool failOnUnhandledPort) : base(machine, failOnUnhandledPort) {
        this.gui = gui;
        if (gui != null) {
            gui.SetOnKeyPressedEvent(() => this.OnKeyEvent());
            gui.SetOnKeyReleasedEvent(() => this.OnKeyEvent());
        }
    }

    public byte? GetScancode() {
        if (gui == null) {
            return null;
        }
        Key? keyCode = gui.GetLastKeyCode();
        byte? scancode = null;
        if (keyCode != null) {
            if (gui.IsKeyPressed(keyCode.Value)) {
                scancode = _keyScancodeConverter.GetKeyPressedScancode(keyCode.Value);
                _logger.Information("Getting scancode. Key pressed {@KeyCode} scancode {@ScanCode}", keyCode, scancode);
            } else {
                scancode = _keyScancodeConverter.GetKeyReleasedScancode(keyCode.Value);
                _logger.Information("Getting scancode. Key released {@KeyCode} scancode {@ScanCode}", keyCode, scancode);
            }

            if (scancode == null) {
                return null;
            }
        }
        return scancode;

    }

    public override byte Inb(int port) {
        byte? scancode = GetScancode();
        if (scancode == null) {
            return 0;
        }

        return scancode.Value;
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(KeyboardIoPort, this);
    }

    public void OnKeyEvent() {
        cpu.ExternalInterrupt(9);
    }
}