namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Very basic implementation of int 11 that basically does nothing.
/// </summary>
public class BiosEquipmentDeterminationInt11Handler : InterruptHandler {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="cpu">The emulated CPU.</param>
    /// <param name="loggerService">The logger service implementation</param>
    public BiosEquipmentDeterminationInt11Handler(IMemory memory, Cpu cpu, ILoggerService loggerService) : base(memory, cpu, loggerService) {
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x11;

    /// <inheritdoc />
    public override void Run() {
        State.AX = 0;
    }
}