namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
///     Handler for interrupt 0x90, which is used by the mouse driver to restore registers.
/// </summary>
public class CustomMouseInt90Handler : InterruptHandler {
    /// <summary>
    ///     The address segment of the callback that is called by the mouse driver.
    /// </summary>
    public const ushort CallAddressSegment = 0xF123;

    /// <summary>
    ///     The address offset of the callback that is called by the mouse driver.
    /// </summary>
    public const ushort CallAddressOffset = 0x0000;

    private readonly IMouseDriver _mouseDriver;

    /// <summary>
    ///     Create a new instance of the <see cref="CustomMouseInt90Handler" /> class.
    /// </summary>
    public CustomMouseInt90Handler(Machine machine, ILoggerService loggerService, IMouseDriver mouseDriver) : base(machine, loggerService) {
        _mouseDriver = mouseDriver;
        // Write a little program that calls this callback and returns from the interrupt.
        _memory.UInt8[CallAddressSegment, CallAddressOffset] = 0xFE; // Custom opcode
        _memory.UInt8[CallAddressSegment, CallAddressOffset + 1] = 0x38;
        _memory.UInt8[CallAddressSegment, CallAddressOffset + 2] = 0x90; // Use callback 0x90 
        _memory.UInt8[CallAddressSegment, CallAddressOffset + 3] = 0xCF; // IRET
    }

    /// <inheritdoc />
    public override byte Index => 0x90;

    /// <inheritdoc />
    public override void Run() {
        _mouseDriver.RestoreRegisters();
    }
}