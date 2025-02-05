namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Interfaces;

/// <summary>
/// Basic implementation of a keyboard
/// </summary>
public sealed class Keyboard : DefaultIOPortHandler {
    private readonly IGui? _gui;
    private readonly A20Gate _a20Gate;
    private readonly DualPic _dualPic;

    /// <summary>
    /// The current keyboard command, such as 'Perform self-test' (0xAA)
    /// </summary>
    public KeyboardCommand Command { get; private set; } = KeyboardCommand.None;

    /// <summary>
    /// Part of the value sent when the CPU reads the status register.
    /// </summary>
    public const byte SystemTestStatusMask = 1<<2;

    /// <summary>
    /// Part of the value sent when the CPU reads the status register.
    /// </summary>
    public const byte KeyboardEnableStatusMask = 1<<4;

    /// <summary>
    /// Initializes a new instance of the <see cref="Keyboard"/> class.
    /// </summary>
    /// <param name="state">The CPU state.</param>
    /// <param name="ioPortDispatcher">The class that is responsible for dispatching ports reads and writes to classes that respond to them.</param>
    /// <param name="a20Gate">The class that controls whether the CPU's 20th address line is enabled.</param>
    /// <param name="dualPic">The two programmable interrupt controllers.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="gui">The graphical user interface. Is null in headless mode.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O port wasn't handled.</param>
    public Keyboard(State state, IOPortDispatcher ioPortDispatcher, A20Gate a20Gate, DualPic dualPic,
        ILoggerService loggerService, IGui? gui, bool failOnUnhandledPort) : base(state, failOnUnhandledPort, loggerService) {
        _gui = gui;
        _a20Gate = a20Gate;
        _dualPic = dualPic;
        if (_gui is not null) {
            _gui.KeyUp += OnKeyUp;
            _gui.KeyDown += OnKeyDown;
        }
        InitPortHandlers(ioPortDispatcher);
    }

    private void OnKeyDown(object? sender, KeyboardEventArgs e) {
        LastKeyboardInput = e;
        _dualPic.ProcessInterruptRequest(1);
    }

    private void OnKeyUp(object? sender, KeyboardEventArgs e) {
        LastKeyboardInput = e;
        _dualPic.ProcessInterruptRequest(1);
    }

    /// <summary>
    /// The latest keyboard event data (refreshed on KeyUp or on KeyDown)
    /// </summary>
    public KeyboardEventArgs LastKeyboardInput { get; private set; } = KeyboardEventArgs.None;

    /// <inheritdoc/>
    public override byte ReadByte(ushort port) {
        byte? scancode = LastKeyboardInput.ScanCode;
        scancode ??= 0;

        return port switch {
            KeyboardPorts.Data => scancode.Value,
            // keyboard not locked, self-test completed.
            KeyboardPorts.StatusRegister => SystemTestStatusMask | KeyboardEnableStatusMask,
            _ => 0
        };
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        switch (port) {
            case KeyboardPorts.Data:
                _a20Gate.IsEnabled = Command switch {
                    KeyboardCommand.SetOutputPort => (value & 2) > 0,
                    KeyboardCommand.EnableA20Gate => false,
                    KeyboardCommand.DisableA20Gate => true,
                    _ => _a20Gate.IsEnabled
                };
                Command = KeyboardCommand.None;
                break;
            case KeyboardPorts.Command:
                if (Enum.IsDefined(typeof(KeyboardCommand), value)) {
                    Command = (KeyboardCommand)value;
                } else {
                    throw new UnrecoverableException("Keyboard command not recognized or not implemented.");
                }
                break;
            default:
                base.WriteByte(port, value);
                break;
        }
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(KeyboardPorts.Data, this);
        ioPortDispatcher.AddIOPortHandler(KeyboardPorts.StatusRegister, this);
    }
}