namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Crude implementation of Int9
/// </summary>
public class BiosKeyboardInt9Handler : InterruptHandler {
    private readonly Keyboard _keyboard;
    private readonly DualPic _dualPic;

    public BiosKeyboardInt9Handler(IMemory memory, Cpu cpu, DualPic dualPic, Keyboard keyboard, BiosDataArea biosDataArea, ILoggerService loggerService) : base(memory, cpu, loggerService) {
        _keyboard = keyboard;
        _dualPic = dualPic;
        BiosKeyboardBuffer = new BiosKeyboardBuffer(memory, biosDataArea);
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
        _dualPic.AcknowledgeInterrupt(1);
    }
}