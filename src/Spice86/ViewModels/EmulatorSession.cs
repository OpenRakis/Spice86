namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels.Services;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Owns the emulator worker thread and exposes lifecycle state through MVVM properties.
/// Constructed AFTER <see cref="ProgramExecutor"/> so no <see cref="Lazy{T}"/> seam is required.
/// </summary>
public sealed partial class EmulatorSession : ObservableObject, IDisposable {
    private readonly ProgramExecutor _programExecutor;
    private readonly IUIDispatcher _uiDispatcher;
    private readonly IExceptionHandler _exceptionHandler;
    private readonly ILoggerService _loggerService;

    private Thread? _emulatorThread;
    private TaskCompletionSource<bool>? _completionSource;
    private bool _disposed;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusMessage = "Emulator: not started.";
    [ObservableProperty] private string _asmOverrideStatus = "ASM Overrides: not used.";

    /// <summary>The exception captured by the worker thread on its last failed run, if any.</summary>
    public Exception? LastException { get; private set; }

    public EmulatorSession(
        ProgramExecutor programExecutor,
        IUIDispatcher uiDispatcher,
        IExceptionHandler exceptionHandler,
        ILoggerService loggerService) {
        _programExecutor = programExecutor;
        _uiDispatcher = uiDispatcher;
        _exceptionHandler = exceptionHandler;
        _loggerService = loggerService;
    }

    /// <summary>
    /// Starts the emulator thread if it is not already running. Returns a task that completes
    /// with the success flag once the worker finishes.
    /// </summary>
    /// <param name="asmOverrideStatusMessage">Status text describing the ASM override mode for this run.</param>
    public Task<bool> StartAsync(string asmOverrideStatusMessage) {
        if (_completionSource is not null) {
            return _completionSource.Task;
        }

        LastException = null;
        StatusMessage = "Emulator starting...";
        AsmOverrideStatus = asmOverrideStatusMessage;

        TaskCompletionSource<bool> completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _completionSource = completionSource;

        _emulatorThread = new Thread(EmulatorThread) { Name = "Emulator" };
        _uiDispatcher.Post(() => IsRunning = true);
        _uiDispatcher.Post(() => StatusMessage = "Emulator started.");
        _emulatorThread.Start();
        return completionSource.Task;
    }

    private void EmulatorThread() {
        TaskCompletionSource<bool>? completionSource = _completionSource;
        try {
            _programExecutor.Run();
            if (_loggerService.IsEnabled(LogLevel.Warning)) {
                _loggerService.LogWarning("Emulation exited. Closing main window...");
            }
            completionSource?.TrySetResult(true);
        } catch (Exception e) {
            if (_loggerService.IsEnabled(LogLevel.Error)) {
                _loggerService.LogError(e, "An error occurred during execution");
            }
            LastException = e;
            _exceptionHandler.Handle(e);
            _uiDispatcher.Post(() => StatusMessage = "Emulator crashed.");
            completionSource?.TrySetResult(false);
        } finally {
            _completionSource = null;
            _uiDispatcher.Post(() => IsRunning = false);
            _uiDispatcher.Post(() => StatusMessage = "Emulator: stopped.");
            _uiDispatcher.Post(() => AsmOverrideStatus = "");
        }
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        if (_emulatorThread?.IsAlive == true) {
            _emulatorThread.Join();
        }
    }
}
