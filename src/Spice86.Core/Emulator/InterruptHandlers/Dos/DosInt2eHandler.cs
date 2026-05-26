namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Shared.Interfaces;

using System;
using System.Diagnostics;

/// <summary>
/// Implements INT 2Eh as the DOS-visible bridge back into the host-managed COMMAND.COM shell.
/// </summary>
public sealed class DosInt2eHandler : InterruptHandler {
    private readonly DosProcessManager _processManager;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public DosInt2eHandler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, Stack stack,
        State state, DosProcessManager processManager, ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _processManager = processManager;
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x2E;

    /// <inheritdoc />
    public override void Run() {
        Debug.Assert(_processManager is not null, "INT 2E requires a process manager.");
        TraceInt2E($"INT 2E enter CS:IP={State.CS:X4}:{State.IP:X4} SS:SP={State.SS:X4}:{State.SP:X4}");
        _processManager.ExecuteCommandComEntry();
        TraceInt2E($"INT 2E exit CS:IP={State.CS:X4}:{State.IP:X4} SS:SP={State.SS:X4}:{State.SP:X4}");
    }

    private void TraceInt2E(string message) {
        if (LoggerService.IsEnabled(LogEventLevel.Debug)) {
            LoggerService.Debug("{Message}", message);
        }

        Debug.WriteLine(message);
        Console.WriteLine(message);
    }
}