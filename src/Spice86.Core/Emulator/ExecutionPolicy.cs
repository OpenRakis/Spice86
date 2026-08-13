using Microsoft.Extensions.Logging;
namespace Spice86.Core.Emulator;


using Spice86.Core.CLI.RuntimeOptions;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Gdb;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

/// <summary>
/// Owns execution-policy concerns for a program run: optional debug-pause breakpoints,
/// the GDB remote-debugging server, and the emulation termination breakpoint.
/// </summary>
internal sealed class ExecutionPolicy : IDisposable {
    private readonly ExecutionPolicyOptions _options;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly IPauseHandler _pauseHandler;
    private readonly EmulationLoop _emulationLoop;
    private readonly GdbServer? _gdbServer;
    private bool _disposed;

    public ExecutionPolicy(
        ExecutionPolicyOptions options,
        IMemory memory,
        IFunctionHandlerProvider functionHandlerProvider,
        State state,
        IPauseHandler pauseHandler,
        EmulatorBreakpointsManager emulatorBreakpointsManager,
        EmulationLoop emulationLoop,
        EmulatorStateSerializer emulatorStateSerializer,
        ILogger loggerService) {
        _options = options;
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _pauseHandler = pauseHandler;
        _emulationLoop = emulationLoop;
        _gdbServer = CreateGdbServer(
            options.GdbServer, memory, functionHandlerProvider, state,
            pauseHandler, emulatorBreakpointsManager, emulatorStateSerializer, loggerService);
    }

    /// <summary>
    /// Installs the debug-mode start/stop pause breakpoints when <see cref="ExecutionPolicyOptions.Debug"/> is set.
    /// </summary>
    public void ApplyStartupBreakpoints() {
        if (!_options.Debug) {
            return;
        }

        ToggleStartOrStopBreakpoint(BreakPointType.MACHINE_START,
            "Starting the emulated program in paused state.");
        ToggleStartOrStopBreakpoint(BreakPointType.MACHINE_STOP,
            "Stopping the emulated program in paused state.");
    }

    /// <summary>
    /// Starts the GDB server if one was configured.
    /// </summary>
    public void StartGdbServer() {
        _gdbServer?.StartServer();
    }

    /// <summary>
    /// Registers a one-shot breakpoint that exits the emulation loop after the configured cycle count.
    /// </summary>
    public void RegisterStopAfterCyclesBreakpoint() {
        long cycles = _options.StopAfterCycles;
        long targetCycles = cycles - 1;
        if (targetCycles <= 0) {
            return;
        }

        AddressBreakPoint breakPoint = new AddressBreakPoint(BreakPointType.CPU_CYCLES, targetCycles,
            _ => _emulationLoop.Exit(), isRemovedOnTrigger: true);
        _emulatorBreakpointsManager.ToggleBreakPoint(breakPoint, true);
    }

    private void ToggleStartOrStopBreakpoint(BreakPointType type, string reason) {
        BreakPoint breakPoint = new UnconditionalBreakPoint(type,
            _ => _pauseHandler.RequestPause(reason), removeOnTrigger: false);
        _emulatorBreakpointsManager.ToggleBreakPoint(breakPoint, true);
    }

    private static GdbServer? CreateGdbServer(
        GdbServerOptions options,
        IMemory memory,
        IFunctionHandlerProvider functionHandlerProvider,
        State state,
        IPauseHandler pauseHandler,
        EmulatorBreakpointsManager emulatorBreakpointsManager,
        EmulatorStateSerializer emulatorStateSerializer,
        ILogger loggerService) {
        if (options.Port == 0) {
            if (loggerService.IsEnabled(LogLevel.Information)) {
                loggerService.LogInformation("GDB port is 0, disabling GDB server.");
            }
            return null;
        }
        return new GdbServer(
            options, memory, functionHandlerProvider, state,
            pauseHandler, emulatorBreakpointsManager, emulatorStateSerializer, loggerService);
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        _gdbServer?.Dispose();
    }
}

