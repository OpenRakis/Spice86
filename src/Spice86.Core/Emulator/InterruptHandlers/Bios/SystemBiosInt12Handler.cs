namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
///     INT 12h handler. Reports how many kb of base memory is installed.
/// </summary>
public class SystemBiosInt12Handler : InterruptHandler {
    private readonly BiosDataArea _biosDataArea;

    /// <summary>
    ///     Initializes a new instance.
    /// </summary>
    /// <param name="memory"></param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="biosDataArea"></param>
    /// <param name="loggerService"></param>
    public SystemBiosInt12Handler(
        IMemory memory, IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state, BiosDataArea biosDataArea, ILoggerService loggerService) 
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _biosDataArea = biosDataArea;
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x12;

    /// <inheritdoc />
    public override void Run() {
        State.AX = _biosDataArea.ConventionalMemorySizeKb;
    }
}