namespace Spice86.Core.Emulator;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.Http;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.DeviceScheduler;
using Spice86.Core.Emulator.VM;
using Spice86.Logging;
using Spice86.Shared.Interfaces;

using System.IO;

/// <summary>
/// Performs the teardown that used to be handled by the program-executor stopped event.
/// </summary>
internal sealed class ShutdownCoordinator : IShutdownCoordinator {
    private readonly DeviceSchedulerThread? _vgaTimingThread;
    private readonly IEmulatedClock _emulatedClock;
    private readonly Spice86HttpApiServer? _httpApiServer;
    private readonly CfgNodeExecutionCompiler _cfgNodeExecutionCompiler;
    private readonly Machine _machine;
    private readonly ILoggerService _loggerService;
    private bool _shutdownCompleted;

    internal ShutdownCoordinator(
        DeviceSchedulerThread? vgaTimingThread,
        IEmulatedClock emulatedClock,
        Spice86HttpApiServer? httpApiServer,
        CfgNodeExecutionCompiler cfgNodeExecutionCompiler,
        Machine machine,
        ILoggerService loggerService) {
        _vgaTimingThread = vgaTimingThread;
        _emulatedClock = emulatedClock;
        _httpApiServer = httpApiServer;
        _cfgNodeExecutionCompiler = cfgNodeExecutionCompiler;
        _machine = machine;
        _loggerService = loggerService;
    }

    public void Shutdown() {
        if (_shutdownCompleted) {
            return;
        }

        _shutdownCompleted = true;

        DisposeSafely(_vgaTimingThread, nameof(_vgaTimingThread));
        DisposeSafely(_emulatedClock, nameof(_emulatedClock));
        DisposeSafely(_httpApiServer, nameof(_httpApiServer));
        DisposeSafely(_cfgNodeExecutionCompiler, nameof(_cfgNodeExecutionCompiler));
        DisposeSafely(_machine, nameof(_machine));
    }

    private void DisposeSafely(IDisposable? disposable, string componentName) {
        if (disposable is null) {
            return;
        }

        try {
            disposable.Dispose();
        } catch (ObjectDisposedException) {
            // Already shut down by a prior path.
        } catch (IOException ex) {
            LogShutdownIssue(ex, componentName);
        } catch (InvalidOperationException ex) {
            LogShutdownIssue(ex, componentName);
        }
    }

    private void LogShutdownIssue(Exception exception, string componentName) {
        if (_loggerService.IsEnabled(LogLevel.Warning)) {
            _loggerService.LogWarning(exception, "Failed to dispose {ComponentName} during shutdown.", componentName);
        }
    }
}
