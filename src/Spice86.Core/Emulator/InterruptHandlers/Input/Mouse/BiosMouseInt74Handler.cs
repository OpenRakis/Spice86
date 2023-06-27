namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
///     Handler for interrupt 0x74, which is used by the BIOS to update the mouse position.
/// </summary>
public class BiosMouseInt74Handler : InterruptHandler {
    private readonly DualPic _hardwareInterruptHandler;
    private readonly IMouseDriver _mouseDriver;

    /// <inheritdoc />
    public BiosMouseInt74Handler(IMouseDriver mouseDriver, DualPic hardwareInterruptHandler, Machine machine, ILoggerService loggerService) : base(machine, loggerService) {
        _mouseDriver = mouseDriver;
        _hardwareInterruptHandler = hardwareInterruptHandler;
    }

    /// <inheritdoc />
    public override byte Index => 0x74;

    /// <inheritdoc />
    public override void Run() {
        _mouseDriver.Update();
        _hardwareInterruptHandler.AcknowledgeInterrupt(12);
    }
}