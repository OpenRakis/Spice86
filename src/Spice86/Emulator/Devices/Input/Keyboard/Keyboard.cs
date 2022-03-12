namespace Spice86.Emulator.Devices.Input.Keyboard;

using Avalonia.Input;

using Serilog;

using Spice86.Emulator.IOPorts;
using Spice86.Emulator.VM;
using Spice86.UI.ViewModels;

/// <summary>
/// Basic implementation of a keyboard
/// </summary>
public class Keyboard : DefaultIOPortHandler {
    private const int KeyboardIoPort = 0x60;
    private static readonly ILogger _logger = Program.Logger.ForContext<Keyboard>();
    private readonly MainWindowViewModel? _gui;

    public bool IsHardwareQueueEmpty => _gui?.IsKeyboardQueueEmpty == true;

    public Keyboard(Machine machine, MainWindowViewModel? gui, Configuration configuration) : base(machine, configuration) {
        _gui = gui;
    }

    public byte? GetScanCode() {
        if (_gui == null) {
            return null;
        }
        (Key, bool)? keyCode = _gui.DequeueLastKeyCode();
        byte? scancode = null;
        if (keyCode != null) {
            if (keyCode.Value.Item2) {
                scancode = KeyScancodeConverter.GetKeyPressedScancode(keyCode.Value.Item1);
                if(_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                    _logger.Information("Getting scancode. Key pressed {@KeyCode} scancode {@ScanCode}", keyCode, scancode);

                }
            } else {
                scancode = KeyScancodeConverter.GetKeyReleasedScancode(keyCode.Value.Item1);
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

    public override byte ReadByte(int port) {
        byte? scancode = GetScanCode();
        if (scancode == null) {
            return 0;
        }
        return scancode.Value;
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(KeyboardIoPort, this);
    }
}