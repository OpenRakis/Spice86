using Microsoft.Extensions.Logging;
namespace Spice86.Core.Emulator.InterruptHandlers.Dos;


using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System;

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
    /// <param name="serviceCallEvents">The service call events the handler reports its service calls to.</param>
    /// <param name="dosInt21Handler">The INT21H is used to exit normally without a process exit code.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosInt20Handler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider,
        Stack stack, State state, ServiceCallEvents serviceCallEvents, DosInt21Handler dosInt21Handler, ILogger loggerService)
        : base(memory, functionHandlerProvider, stack, state, serviceCallEvents, loggerService) {
        _dosInt21Handler = dosInt21Handler;
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x20;

    /// <inheritdoc />
    [InterruptService(Reads = CpuValues.Al, Writes = CpuValues.None,
        Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(Terminate), Inputs = [CpuValues.Al],
        Transfer = ServiceTransferKind.Terminates)]
    public override void Run() {
        if (LoggerService.IsEnabled(LogLevel.Information)) {
            LoggerService.LogInformation("INT 20h: PROGRAM TERMINATE (legacy CP/M INT20H handler)");
        }

        // FreeDOS calls INT 21h AH=0 to legacy CP/M programs termination
        State.AH = 0x00;
        _dosInt21Handler.QuitWithExitCode();
    }

    /// <summary>
    /// Generated-code implementation of INT 20h: terminates the current process with
    /// <paramref name="exitCode"/> like INT 21h AH=4Ch. Never returns to the call site.
    /// </summary>
    /// <remarks>
    /// Replicates the interpreted entry: <see cref="Run"/>'s AH=00h dispatch into
    /// <see cref="DosInt21Handler.QuitWithExitCode"/>, which bypasses the INT 21h caller-stack
    /// save; the shared terminate path pushes the <c>int</c>'s frame.
    /// </remarks>
    public Action Terminate(byte exitCode) {
        State.AH = 0x00;
        return _dosInt21Handler.TerminateWithoutCallerStackRecord(exitCode);
    }
}
