namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Shared.Interfaces;

/// <summary>
/// Implements INT 20h - Program Terminate.
/// </summary>
/// <remarks>
/// <para>
/// INT 20h is the legacy DOS program termination interrupt, primarily used by COM programs.
/// It terminates the current program and returns control to the parent process.
/// </para>
/// <para>
/// <strong>Important:</strong> INT 20h requires CS to point to the PSP segment.
/// This is automatic for COM files but may not be true for EXE files.
/// Programs should use INT 21h/4Ch instead for reliable termination.
/// </para>
/// <para>
/// The termination process:
/// <list type="bullet">
/// <item>Exit code is 0 (no way to specify exit code with INT 20h)</item>
/// <item>All memory owned by the process is freed</item>
/// <item>Interrupt vectors 22h, 23h, 24h are restored from PSP</item>
/// <item>Control returns to parent via INT 22h vector</item>
/// </list>
/// </para>
/// <para>
/// <strong>MCB Note:</strong> The PSP segment is determined from CS on INT 20h entry.
/// In real DOS, CS must equal the PSP segment for correct operation. This implementation
/// follows the same behavior.
/// </para>
/// </remarks>
public class DosInt20Handler : InterruptHandler {
    private readonly DosProcessManager _dosProcessManager;
    private readonly InterruptVectorTable _interruptVectorTable;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="dosProcessManager">The DOS process manager for termination handling.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosInt20Handler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, 
        Stack stack, State state, DosProcessManager dosProcessManager, ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _dosProcessManager = dosProcessManager;
        _interruptVectorTable = new InterruptVectorTable(memory);
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x20;

    /// <inheritdoc />
    public override void Run() {
        if (LoggerService.IsEnabled(LogEventLevel.Information)) {
            LoggerService.Information("INT 20h: PROGRAM TERMINATE (legacy)");
        }
        
        // INT 20h always exits with code 0 (no way to specify exit code)
        // Termination type is Normal
        bool shouldContinue = _dosProcessManager.TerminateProcess(
            0x00,  // Exit code 0
            DosTerminationType.Normal,
            _interruptVectorTable);

        if (!shouldContinue) {
            // No parent to return to - stop emulation
            State.IsRunning = false;
        }
    }
}