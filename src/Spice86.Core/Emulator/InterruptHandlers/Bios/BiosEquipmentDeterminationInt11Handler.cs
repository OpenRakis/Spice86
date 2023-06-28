namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Very basic implementation of int 11 that basically does nothing.
/// </summary>
public class BiosEquipmentDeterminationInt11Handler : InterruptHandler {
    public BiosEquipmentDeterminationInt11Handler(Machine machine, ILoggerService loggerService) : base(machine, loggerService) {
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x11;

    /// <inheritdoc />
    public override void Run() {
        _state.AX = 0;
    }
}