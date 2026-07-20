namespace Spice86.Core.Emulator;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.StateSerialization;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Runs the loaded program inside the emulator. Acts as a thin facade composing
/// <see cref="ProgramBootstrapper"/>, <see cref="ExecutionPolicy"/>, the emulation loop,
/// state serialization, and the shutdown coordinator.
/// </summary>
public sealed class ProgramExecutor : IDisposable {
    private bool _disposed;
    private readonly ILoggerService _loggerService;
    private readonly EmulationLoop _emulationLoop;
    private readonly EmulatorStateSerializer _emulatorStateSerializer;
    private readonly IShutdownCoordinator _shutdownCoordinator;
    private readonly ExecutionPolicy _executionPolicy;

    /// <summary>
    /// Initializes a new instance of <see cref="ProgramExecutor"/>. The initial program is loaded eagerly
    /// from <paramref name="programBootstrapper"/> so that <see cref="Run"/> only deals with execution.
    /// </summary>
    /// <param name="programBootstrapper">Loads the configured executable into memory.</param>
    /// <param name="executionPolicy">Owns GDB, stop-after-cycles, and debug-pause breakpoints.</param>
    /// <param name="emulationLoop">The core emulation loop driver.</param>
    /// <param name="emulatorStateSerializer">Writes the recorded emulation state on normal exit.</param>
    /// <param name="shutdownCoordinator">Coordinates idempotent post-run teardown.</param>
    /// <param name="loggerService">The logging service to use.</param>
    internal ProgramExecutor(
        ProgramBootstrapper programBootstrapper,
        ExecutionPolicy executionPolicy,
        EmulationLoop emulationLoop,
        EmulatorStateSerializer emulatorStateSerializer,
        IShutdownCoordinator shutdownCoordinator,
        ILoggerService loggerService) {
        _emulationLoop = emulationLoop;
        _emulatorStateSerializer = emulatorStateSerializer;
        _shutdownCoordinator = shutdownCoordinator;
        _executionPolicy = executionPolicy;
        _loggerService = loggerService;
        programBootstrapper.LoadInitialProgram();
    }

    /// <summary>
    /// Starts the loaded program.
    /// </summary>
    public void Run() {
        try {
            if (_loggerService.IsEnabled(LogLevel.Information)) {
                _loggerService.LogInformation("Starting the emulation loop");
            }

            _executionPolicy.ApplyStartupBreakpoints();
            _executionPolicy.StartGdbServer();
            _executionPolicy.RegisterStopAfterCyclesBreakpoint();

            _emulationLoop.Run();

            _emulatorStateSerializer.EmulationStateDataWriter.Write();
        } finally {
            _shutdownCoordinator.Shutdown();
        }
    }

    /// <inheritdoc cref="IDisposable" />
    public void Dispose() {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if (_disposed) {
            return;
        }
        if (disposing) {
            _executionPolicy.Dispose();
            _emulationLoop.Exit();
        }
        _disposed = true;
    }
}
