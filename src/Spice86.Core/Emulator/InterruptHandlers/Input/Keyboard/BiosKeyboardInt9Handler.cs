﻿namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Crude implementation of BIOS keyboard buffer handler (interrupt 0x9)
/// </summary>
public class BiosKeyboardInt9Handler : InterruptHandler {
    private readonly Keyboard _keyboard;
    private readonly DualPic _dualPic;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="cpu">The emulated CPU.</param>
    /// <param name="dualPic">The two programmable interrupt controllers.</param>
    /// <param name="keyboard">The keyboard controller.</param>
    /// <param name="biosDataArea">The memory mapped BIOS values.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public BiosKeyboardInt9Handler(IMemory memory, Cpu cpu, DualPic dualPic, Keyboard keyboard, BiosDataArea biosDataArea, ILoggerService loggerService) : base(memory, cpu, loggerService) {
        _keyboard = keyboard;
        _dualPic = dualPic;
        BiosKeyboardBuffer = new BiosKeyboardBuffer(memory, biosDataArea);
        BiosKeyboardBuffer.Init();
    }

    /// <summary>
    /// Gets the BIOS keyboard buffer.
    /// </summary>
    public BiosKeyboardBuffer BiosKeyboardBuffer { get; }

    /// <inheritdoc />
    public override byte VectorNumber => 0x9;

    /// <inheritdoc />
    public override void Run() {
        if (_keyboard.LastKeyboardInput.ScanCode is null) {
            return;
        }

        byte ascii = _keyboard.LastKeyboardInput.AsciiCode ?? 0;

        if(LoggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            LoggerService.Verbose("{BiosInt9KeyReceived}", ascii);
        }

        BiosKeyboardBuffer.EnqueueKeyCode((ushort)(_keyboard.LastKeyboardInput.ScanCode.Value << 8 | ascii));
        _dualPic.AcknowledgeInterrupt(1);
    }
}