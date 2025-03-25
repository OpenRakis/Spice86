namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// <para>Reimplementation of int28</para>
/// <para>This is a way of letting DOS know that the application is idle and that it can perform other tasks.</para>
/// </summary>
public class DosInt28Handler : InterruptHandler {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosInt28Handler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state
        , ILoggerService loggerService) : base(memory, functionHandlerProvider, stack, state, loggerService) {
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x28;

    /// <inheritdoc />
    public override void Run() {
        LoggerService.Verbose("DOS IDLE");
    }
}