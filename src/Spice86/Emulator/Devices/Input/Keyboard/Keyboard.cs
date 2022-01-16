namespace Spice86.Emulator.Devices.Input.Keyboard;

using Avalonia.Input;

using Serilog;

using Spice86.Emulator.IOPorts;
using Spice86.Emulator.Machine;
using Spice86.Ui;

/// <summary>
/// Basic implementation of a keyboard
/// </summary>
public class Keyboard : DefaultIOPortHandler {
    private const int KeyboardIoPort = 0x60;
    private static readonly ILogger _logger = Log.Logger.ForContext<Keyboard>();
    private readonly KeyScancodeConverter _keyScancodeConverter = new();
    private readonly Gui gui;

    public Keyboard(Machine machine, Gui gui, bool failOnUnhandledPort) : base(machine, failOnUnhandledPort) {
        this.gui = gui;
        if (gui != null) {
            gui.SetOnKeyPressedEvent(() => this.OnKeyEvent());
            gui.SetOnKeyReleasedEvent(() => this.OnKeyEvent());
        }
    }

    public int? GetScancode() {
        Key keyCode = gui.GetLastKeyCode();
        int? scancode;
        if (gui.IsKeyPressed(keyCode)) {
            scancode = _keyScancodeConverter.GetKeyPressedScancode(keyCode);
            _logger.Information("Getting scancode. Key pressed {@KeyCode} scancode {@ScanCode}", keyCode, scancode);
        } else {
            scancode = _keyScancodeConverter.GetKeyReleasedScancode(keyCode);
            _logger.Information("Getting scancode. Key released {@KeyCode} scancode {@ScanCode}", keyCode, scancode);
        }

        if (scancode == null) {
            return null;
        }

        return (byte)scancode;
    }

    public override int Inb(int port) {
        int? scancode = GetScancode();
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