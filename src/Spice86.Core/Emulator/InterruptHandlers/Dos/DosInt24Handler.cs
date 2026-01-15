namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Implements INT 24h - Critical error handler.
/// Returns "Fail" (AL=3) to propagate the error back to DOS callers.
/// </summary>
public class DosInt24Handler : InterruptHandler {
    public DosInt24Handler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state,
        ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
    }

    public override byte VectorNumber => 0x24;

    public override void Run() {
        if (LoggerService.IsEnabled(LogEventLevel.Information)) {
            LoggerService.Information("INT 24h: Critical error handler invoked, returning FAIL (AL=3).");
        }

        State.AL = 0x03;
    }
}
