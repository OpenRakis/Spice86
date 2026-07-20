namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Shared.Interfaces;

/// <summary>
/// Implements INT 23h - Control-Break handler.
/// Terminates the current process with Ctrl+C termination type.
/// </summary>
public class DosInt23Handler : InterruptHandler {
    private readonly DosProcessManager _dosProcessManager;

    public DosInt23Handler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state,
        DosProcessManager dosProcessManager, ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _dosProcessManager = dosProcessManager;
    }

    public override byte VectorNumber => 0x23;

    public override void Run() {
        if (LoggerService.IsEnabled(LogLevel.Information)) {
            LoggerService.LogInformation("INT 23h: Control-Break handler invoked, terminating current process with Ctrl+C status.");
        }

        _dosProcessManager.TerminateProcess(0xFF, DosTerminationType.CtrlC);
    }
}
