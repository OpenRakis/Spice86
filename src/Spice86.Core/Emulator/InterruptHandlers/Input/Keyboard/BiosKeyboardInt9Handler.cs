namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Crude implementation of Int9
/// </summary>
public class BiosKeyboardInt9Handler : InterruptHandler {
    private readonly IKeyboardDevice _keyboard;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="keyboard">The keyboard device.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public BiosKeyboardInt9Handler(Machine machine, IKeyboardDevice keyboard, ILoggerService loggerService) : base(machine, loggerService) {
        _keyboard = keyboard;
        BiosKeyboardBuffer = new BiosKeyboardBuffer(machine.Memory);
    }

    /// <summary>
    /// The keyboard input buffer of ASCII scan codes
    /// </summary>
    public BiosKeyboardBuffer BiosKeyboardBuffer { get; }

    /// <inheritdoc />
    public override byte Index => 0x9;

    /// <inheritdoc />
    public override void Run() {
        byte? scancode = _keyboard.Input.ScanCode;
        if (scancode is null) {
            return;
        }

        byte ascii = _keyboard.Input.AsciiCode ?? 0;

        if(_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("{BiosInt9KeyReceived}", ascii);
        }

        BiosKeyboardBuffer.AddKeyCode((ushort)(scancode.Value << 8 | ascii));
        _machine.ProgrammableSubsystem.DualPic.AcknowledgeInterrupt(1);
    }
}