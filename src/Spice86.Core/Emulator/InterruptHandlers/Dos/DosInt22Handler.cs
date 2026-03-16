namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Shared.Interfaces;

/// <summary>
/// Implements INT 22h - Terminate address handler.
/// When invoked directly, terminates the current process normally.
/// </summary>
public class DosInt22Handler : InterruptHandler {
    private readonly DosProcessManager _dosProcessManager;

    public DosInt22Handler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state,
        DosProcessManager dosProcessManager, ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _dosProcessManager = dosProcessManager;
    }

    public override byte VectorNumber => 0x22;

    public override void Run() {
        if (LoggerService.IsEnabled(LogEventLevel.Information)) {
            LoggerService.Information("INT 22h: Terminate address invoked, terminating current process.");
        }

        _dosProcessManager.TerminateProcess(0, DosTerminationType.Normal);
    }
}
