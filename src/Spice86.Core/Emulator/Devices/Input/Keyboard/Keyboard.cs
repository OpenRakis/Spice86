namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Serilog;

using Spice86.Core.Emulator;

using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Logging;
using Spice86.Shared;
using Spice86.Shared.Interfaces;

/// <summary>
/// Basic implementation of a keyboard
/// </summary>
public class Keyboard : DefaultIOPortHandler {
    private const int KeyboardIoPort = 0x60;
    private static readonly ILogger _logger = Serilogger.Logger.ForContext<Keyboard>();
    private readonly IGui? _gui;
    private readonly IKeyScanCodeConverter? _keyScanCodeConverter;

    public Keyboard(Machine machine, IGui? gui, IKeyScanCodeConverter? keyScanCodeConverter, Configuration configuration) : base(machine, configuration) {
        _gui = gui;
        _keyScanCodeConverter = keyScanCodeConverter;
        if (_gui is not null) {
            _gui.KeyUp += OnKeyUp;
            _gui.KeyDown += OnKeyDown;
        }
    }

    private void OnKeyDown(object? sender, EventArgs e) {
        LastKeyboardInput = new(e, true);
        RaiseIrq();
    }

    private void RaiseIrq() {
        _machine.DualPic.ProcessInterruptRequest(1);
    }

    private void OnKeyUp(object? sender, EventArgs e) {
        LastKeyboardInput = new(e, false);
        RaiseIrq();
    }

    public KeyboardInput? LastKeyboardInput { get; private set; } = null;

    public int LastKeyRepeatCount { get; private set; }

    public byte? GetScanCode() {
        if (_gui == null) {
            return null;
        }
        byte? scancode = null;
        if (LastKeyboardInput is not null) {
            KeyboardInput lastKeyboardInput = (KeyboardInput)LastKeyboardInput;
            if (lastKeyboardInput.IsPressed) {
                scancode = _keyScanCodeConverter?.GetKeyPressedScancode(lastKeyboardInput);
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                    _logger.Information("Getting scancode. Key pressed {@KeyCode} scancode {@ScanCode}", LastKeyboardInput.Value.EventArgs, scancode);

                }
            } else {
                scancode = _keyScanCodeConverter?.GetKeyReleasedScancode(lastKeyboardInput);
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                    _logger.Information("Getting scancode. Key released {@KeyCode} scancode {@ScanCode}", LastKeyboardInput.Value.EventArgs, scancode);
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