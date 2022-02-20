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
    private readonly IVideoKeyboardMouseIO? _gui;

    public Keyboard(Machine machine, IVideoKeyboardMouseIO? gui, bool failOnUnhandledPort) : base(machine, failOnUnhandledPort) {
        _gui = gui;
        if (gui != null) {
            gui.SetOnKeyPressedEvent(() => this.OnKeyEvent());
            gui.SetOnKeyReleasedEvent(() => this.OnKeyEvent());
        }
    }

    public byte? GetScancode() {
        if (_gui == null) {
            return null;
        }
        Key? keyCode = _gui.GetLastKeyCode();
        byte? scancode = null;
        if (keyCode != null) {
            if (_gui.IsKeyPressed(keyCode.Value)) {
                scancode = KeyScancodeConverter.GetKeyPressedScancode(keyCode.Value);
                if(_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                    _logger.Information("Getting scancode. Key pressed {@KeyCode} scancode {@ScanCode}", keyCode, scancode);

                }
            } else {
                scancode = KeyScancodeConverter.GetKeyReleasedScancode(keyCode.Value);
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                    _logger.Information("Getting scancode. Key released {@KeyCode} scancode {@ScanCode}", keyCode, scancode);
                }
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
        _cpu.ExternalInterrupt(9);
    }
}