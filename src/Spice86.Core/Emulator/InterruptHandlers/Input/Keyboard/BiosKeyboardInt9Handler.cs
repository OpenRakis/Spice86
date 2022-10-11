namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Serilog;

using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.VM;
using Spice86.Logging;
using Spice86.Shared.Interfaces;

/// <summary>
/// Crude implementation of Int9
/// </summary>
public class BiosKeyboardInt9Handler : InterruptHandler {
    private readonly ILogger _logger = Serilogger.Logger.ForContext<BiosKeyboardInt9Handler>();

    private readonly Keyboard _keyboard;
    private readonly IKeyScanCodeConverter? _keyScanCodeConverter;

    public BiosKeyboardInt9Handler(Machine machine, IKeyScanCodeConverter? keyScanCodeConverter) : base(machine) {
        _keyboard = machine.Keyboard;
        _keyScanCodeConverter = keyScanCodeConverter;
        BiosKeyboardBuffer = new BiosKeyboardBuffer(machine.Memory);
        BiosKeyboardBuffer.Init();
    }

    public BiosKeyboardBuffer BiosKeyboardBuffer { get; }

    public override byte Index => 0x9;

    public override void Run() {
        byte? scancode = _keyboard.GetScanCode();
        if (scancode is null) {
            return;
        }

        byte ascii = (_keyScanCodeConverter?.GetAsciiCode(scancode.Value)) ?? 0;

        if(_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("{@BiosInt9KeyReceived}", ascii);
        }

        BiosKeyboardBuffer.AddKeyCode((ushort)(scancode.Value << 8 | ascii));
    }
}