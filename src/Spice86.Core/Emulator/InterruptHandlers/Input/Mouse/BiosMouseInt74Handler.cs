namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
///     Handler for interrupt 0x74, which is used by the BIOS to update the mouse position.
/// </summary>
public class BiosMouseInt74Handler : InterruptHandler {
    /// <inheritdoc />
    public BiosMouseInt74Handler(Machine machine, ILoggerService loggerService) : base(machine, loggerService) {
    }

    /// <inheritdoc />
    public override byte Index => 0x74;

    /// <inheritdoc />
    public override void Run() {
        _machine.MouseDriver.Update();
        _machine.DualPic.AcknowledgeInterrupt(12);
    }
}