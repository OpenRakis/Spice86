namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Implements INT 20h - Program Terminate.
/// </summary>
public class DosInt20Handler : InterruptHandler {
    private readonly DosInt21Handler _dosInt21Handler;
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="dosInt21Handler">The INT21H is used to exit normally without a process exit code.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosInt20Handler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, 
        Stack stack, State state, DosInt21Handler dosInt21Handler, ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _dosInt21Handler = dosInt21Handler;
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x20;

    /// <inheritdoc />
    public override void Run() {
        if (LoggerService.IsEnabled(LogEventLevel.Information)) {
            LoggerService.Information("INT 20h: PROGRAM TERMINATE (legacy CP/M INT20H handler)");
        }
        
        // FreeDOS calls INT 21h AH=0 to legacy CP/M programs termination
        State.AH = 0x00;
        _dosInt21Handler.QuitWithExitCode();
    }
}