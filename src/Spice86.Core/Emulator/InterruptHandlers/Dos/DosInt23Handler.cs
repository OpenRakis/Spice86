using Microsoft.Extensions.Logging;
namespace Spice86.Core.Emulator.InterruptHandlers.Dos;


using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System;

/// <summary>
/// Implements INT 23h - Control-Break handler.
/// Terminates the current process with Ctrl+C termination type.
/// </summary>
public class DosInt23Handler : InterruptHandler {
    private readonly DosProcessManager _dosProcessManager;

    public DosInt23Handler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state,
        DosProcessManager dosProcessManager, ILogger loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _dosProcessManager = dosProcessManager;
    }

    public override byte VectorNumber => 0x23;

    /// <inheritdoc />

    [InterruptService(Reads = CpuValues.None, Writes = CpuValues.None,
        Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(Terminate), Inputs = [],
        Transfer = ServiceTransferKind.Terminates)]
    public override void Run() {
        if (LoggerService.IsEnabled(LogLevel.Information)) {
            LoggerService.LogInformation("INT 23h: Control-Break handler invoked, terminating current process with Ctrl+C status.");
        }

        _dosProcessManager.TerminateProcess(0xFF, DosTerminationType.CtrlC);
    }

    /// <summary>
    /// Generated-code implementation of a direct INT 23h: terminates the current process with
    /// the Ctrl-C termination type. Never returns to the call site.
    /// </summary>
    public Action Terminate() {
        ushort terminatedPsp = new DosSwappableDataArea(Memory,
            MemoryUtils.ToPhysicalAddress(DosSwappableDataArea.BaseSegment, 0))
            .CurrentProgramSegmentPrefix;
        SegmentedAddress ipBefore = State.IpSegmentedAddress;
        PushInterruptFrame();
        _dosProcessManager.TerminateProcess(0xFF, DosTerminationType.CtrlC);
        return ExitOrThrowProcessTerminated(terminatedPsp, ipBefore, true);
    }
}

