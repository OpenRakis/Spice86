namespace Spice86.ViewModels.Services;

using Avalonia.Controls;

using Spice86.Core.Emulator.StateSerialization;
using Spice86.Views;

using System;

/// <summary>
/// Shows <see cref="DumpProgressWindow"/> while the recorded-data dump runs and feeds it the events of
/// <see cref="DumpProgressReporter"/>. The reporter runs on the emulator thread; every update is posted
/// to the UI thread through the dispatcher.
/// </summary>
internal sealed class DumpProgressUiService : IDisposable {
    private readonly record struct PendingStep(DumpPhase Phase, int Current, int Total, string Item);

    private readonly DumpProgressReporter _reporter;
    private readonly IUIDispatcher _uiDispatcher;
    private readonly Window _owner;
    private readonly object _stepLock = new();
    private DumpProgressWindow? _window;
    private DumpProgressViewModel? _viewModel;
    private PendingStep? _pendingStep;
    private bool _stepPostPending;

    public DumpProgressUiService(DumpProgressReporter reporter, IUIDispatcher uiDispatcher, Window owner) {
        _reporter = reporter;
        _uiDispatcher = uiDispatcher;
        _owner = owner;
        _reporter.DumpStarted += OnDumpStarted;
        _reporter.MessageLogged += OnMessageLogged;
        _reporter.StepReported += OnStepReported;
        _reporter.DumpFinished += OnDumpFinished;
        _reporter.PhaseBegan += OnPhaseBegan;
    }

    private void OnPhaseBegan(DumpPhase phase) =>
        _uiDispatcher.Post(() => _viewModel?.BeginPhase());

    private void OnDumpStarted() => _uiDispatcher.Post(() => {
        _viewModel = new DumpProgressViewModel();
        _window = new DumpProgressWindow {
            DataContext = _viewModel
        };
        _ = _window.ShowDialog(_owner);
    });

    private void OnMessageLogged(string message) =>
        _uiDispatcher.Post(() => _viewModel?.AddMessage(message));

    /// <summary>
    /// Coalesces steps to the latest one: a step already queued for the UI thread is replaced instead of
    /// stacking up posts. The step naming the currently emitting method always reaches the window.
    /// </summary>
    private void OnStepReported(DumpPhase phase, int current, int total, string item) {
        lock (_stepLock) {
            _pendingStep = new PendingStep(phase, current, total, item);
            if (_stepPostPending) {
                return;
            }
            _stepPostPending = true;
        }
        _uiDispatcher.Post(PostPendingStep);
    }

    private void PostPendingStep() {
        PendingStep? step;
        lock (_stepLock) {
            step = _pendingStep;
            _pendingStep = null;
            _stepPostPending = false;
        }
        if (step is PendingStep value) {
            _viewModel?.ReportStep(value.Phase, value.Current, value.Total, value.Item);
        }
    }

    private void OnDumpFinished() => _uiDispatcher.Post(() => {
        _viewModel?.Finish();
        _window?.Close();
        _window = null;
        _viewModel = null;
    });

    /// <inheritdoc />
    public void Dispose() {
        _reporter.DumpStarted -= OnDumpStarted;
        _reporter.MessageLogged -= OnMessageLogged;
        _reporter.StepReported -= OnStepReported;
        _reporter.DumpFinished -= OnDumpFinished;
        _reporter.PhaseBegan -= OnPhaseBegan;
        DumpProgressWindow? window = _window;
        _window = null;
        _viewModel = null;
        if (window is not null) {
            if (_uiDispatcher.CheckAccess()) {
                window.Close();
            } else {
                _uiDispatcher.Post(window.Close);
            }
        }
    }
}
