using Spice86.Logging;

namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Serilog;

using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Crude implementation of Int9
/// </summary>
public class BiosKeyboardInt9Handler : InterruptHandler {
    private readonly ILoggerService _loggerService;

    private readonly Keyboard _keyboard;
    private readonly IKeyScanCodeConverter? _keyScanCodeConverter;

    public BiosKeyboardInt9Handler(Machine machine, ILoggerService loggerService, IKeyScanCodeConverter? keyScanCodeConverter) : base(machine) {
        _loggerService = loggerService;
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

        if(_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("{@BiosInt9KeyReceived}", ascii);
        }

        BiosKeyboardBuffer.AddKeyCode((ushort)(scancode.Value << 8 | ascii));
    }
}