namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Reimplementation of int20
/// </summary>
public class DosInt20Handler : InterruptHandler {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosInt20Handler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state, ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x20;

    /// <inheritdoc />
    public override void Run() {
        LoggerService.Verbose("PROGRAM TERMINATE");
        State.IsRunning = false;
    }
}