namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
///     Handler for interrupt 0x90, which is used by the mouse driver to restore registers.
/// </summary>
public class CustomMouseInt90Handler : InterruptHandler {
    private readonly IMouseDriver _mouseDriver;

    /// <summary>
    ///     Create a new instance of the <see cref="CustomMouseInt90Handler" /> class.
    /// </summary>
    public CustomMouseInt90Handler(IMouseDriver mouseDriver, Machine machine, ILoggerService loggerService) : base(machine, loggerService) {
        _mouseDriver = mouseDriver;
    }

    /// <inheritdoc />
    public override byte Index => 0x90;

    /// <inheritdoc />
    public override void Run() {
        _mouseDriver.RestoreRegisters();
    }
}