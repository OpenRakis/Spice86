namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Interfaces;

/// <summary>
/// Basic implementation of a keyboard
/// </summary>
public class Keyboard : DefaultIOPortHandler {
    private const int KeyboardIoPort = 0x60;
    private readonly IGui? _gui;
    private readonly IKeyScanCodeConverter? _keyScanCodeConverter;

    public Keyboard(Machine machine, ILoggerService loggerService, IGui? gui, IKeyScanCodeConverter? keyScanCodeConverter, Configuration configuration) : base(machine, configuration, loggerService) {
        _gui = gui;
        _keyScanCodeConverter = keyScanCodeConverter;
        if (_gui is not null) {
            _gui.KeyUp += OnKeyUp;
            _gui.KeyDown += OnKeyDown;
        }
    }

    private void OnKeyDown(object? sender, KeyboardEventArgs e) {
        LastKeyboardInput = e;
        RaiseIrq();
    }

    private void RaiseIrq() {
        _machine.DualPic.ProcessInterruptRequest(1);
    }

    private void OnKeyUp(object? sender, KeyboardEventArgs e) {
        LastKeyboardInput = e;
        RaiseIrq();
    }

    public KeyboardEventArgs? LastKeyboardInput { get; private set; }

    public byte? GetScanCode() {
        if (_gui == null) {
            return null;
        }
        byte? scancode = null;
        if (LastKeyboardInput is not null) {
            KeyboardEventArgs lastKeyboardInput = (KeyboardEventArgs)LastKeyboardInput;
            if (lastKeyboardInput.IsPressed) {
                scancode = _keyScanCodeConverter?.GetKeyPressedScancode(lastKeyboardInput);
                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                    _loggerService.Verbose("Getting scancode. Key pressed {KeyCode} scancode {ScanCode}", LastKeyboardInput.Value, scancode);
                }
            } else {
                scancode = _keyScanCodeConverter?.GetKeyReleasedScancode(lastKeyboardInput);
                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                    _loggerService.Verbose("Getting scancode. Key released {KeyCode} scancode {ScanCode}", LastKeyboardInput.Value, scancode);
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