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

    /// <summary>
    /// Initializes a new instance of the <see cref="Keyboard"/> class.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="gui">The graphical user interface. Is null in headless mode.</param>
    /// <param name="configuration">The emulator configuration.</param>
    public Keyboard(Machine machine, ILoggerService loggerService, IGui? gui, Configuration configuration) : base(machine, configuration, loggerService) {
        _gui = gui;
        if (_gui is not null) {
            _gui.KeyUp += OnKeyUp;
            _gui.KeyDown += OnKeyDown;
        }
    }

    private void OnKeyDown(object? sender, KeyboardEventArgs e) {
        LastKeyboardInput = e;
        _machine.DualPic.ProcessInterruptRequest(1);
    }

    private void OnKeyUp(object? sender, KeyboardEventArgs e) {
        LastKeyboardInput = e;
        _machine.DualPic.ProcessInterruptRequest(1);
    }

    /// <summary>
    /// The latest keyboard event data (refreshed on KeyUp or on KeyDown)
    /// </summary>
    public KeyboardEventArgs LastKeyboardInput { get; private set; } = KeyboardEventArgs.None;

    /// <inheritdoc/>
    public override byte ReadByte(int port) {
        byte? scancode = LastKeyboardInput.ScanCode;
        if (scancode == null) {
            return 0;
        }
        return scancode.Value;
    }

    /// <inheritdoc/>
    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(KeyboardIoPort, this);
    }
}