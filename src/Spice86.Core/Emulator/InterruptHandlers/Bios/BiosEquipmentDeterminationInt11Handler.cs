namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
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
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="loggerService">The logger service implementation</param>
    public BiosEquipmentDeterminationInt11Handler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state, ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x11;

    /// <inheritdoc />
    public override void Run() {
        State.AX = 0;
    }
}