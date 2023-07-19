namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Crude implementation of Int9
/// </summary>
public class BiosKeyboardInt9Handler : InterruptHandler {
    private readonly Keyboard _keyboard;

    public BiosKeyboardInt9Handler(Machine machine, BiosDataArea biosDataArea, ILoggerService loggerService) : base(machine, loggerService) {
        _keyboard = machine.Keyboard;
        BiosKeyboardBuffer = new BiosKeyboardBuffer(machine.Memory, biosDataArea);
        BiosKeyboardBuffer.Init();
    }

    public BiosKeyboardBuffer BiosKeyboardBuffer { get; }

    /// <inheritdoc />
    public override byte VectorNumber => 0x9;

    /// <inheritdoc />
    public override void Run() {
        byte? scancode = _keyboard.LastKeyboardInput.ScanCode;
        if (scancode is null) {
            return;
        }

        byte ascii = _keyboard.LastKeyboardInput.AsciiCode ?? 0;

        if(_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("{BiosInt9KeyReceived}", ascii);
        }

        BiosKeyboardBuffer.EnqueueKeyCode((ushort)(scancode.Value << 8 | ascii));
        _machine.DualPic.AcknowledgeInterrupt(1);
    }
}