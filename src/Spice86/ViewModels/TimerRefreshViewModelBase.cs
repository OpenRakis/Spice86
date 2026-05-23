namespace Spice86.ViewModels;

using Avalonia.Threading;

using Spice86.Core.Emulator.VM;
using Spice86.ViewModels.Services;

/// <summary>
/// Base class for view-models refreshed by a dispatcher timer while visible.
/// Supports optional once-per-pause refresh semantics when a pause handler is provided.
/// </summary>
public abstract class TimerRefreshViewModelBase : ViewModelBase,
    IEmulatorObjectViewModel, IDebuggerTabContentViewModel, IDisposable {
    private readonly DispatcherTimer _refreshTimer;
    private readonly IPauseHandler? _pauseHandler;
    private bool _isVisible;
    private bool _hasRefreshedDuringCurrentPause;
    private bool _isDisposed;

    /// <inheritdoc />
    public abstract string Header { get; }

    /// <inheritdoc />
    public bool IsVisible {
        get => _isVisible;
        set {
            if (_isVisible == value) {
                return;
            }

            _isVisible = value;

            if (!_isVisible) {
                _hasRefreshedDuringCurrentPause = false;
                return;
            }

            if (_pauseHandler is not null && _pauseHandler.IsPaused) {
                _hasRefreshedDuringCurrentPause = false;
            }

            UpdateValues(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Initializes periodic refresh while visible.
    /// </summary>
    /// <param name="refreshIntervalMs">Polling period in milliseconds.</param>
    protected TimerRefreshViewModelBase(int refreshIntervalMs) {
        _pauseHandler = null;
        _refreshTimer = DispatcherTimerStarter.StartNewDispatcherTimer(
            TimeSpan.FromMilliseconds(refreshIntervalMs),
            DispatcherPriority.Background,
            UpdateValues);
    }

    /// <summary>
    /// Initializes periodic refresh while visible with once-per-pause semantics.
    /// </summary>
    /// <param name="refreshIntervalMs">Polling period in milliseconds.</param>
    /// <param name="pauseHandler">Pause state source.</param>
    protected TimerRefreshViewModelBase(int refreshIntervalMs, IPauseHandler pauseHandler) {
        _pauseHandler = pauseHandler;
        _pauseHandler.Resumed += OnResumed;
        _refreshTimer = DispatcherTimerStarter.StartNewDispatcherTimer(
            TimeSpan.FromMilliseconds(refreshIntervalMs),
            DispatcherPriority.Background,
            UpdateValues);
    }

    /// <inheritdoc />
    public void UpdateValues(object? sender, EventArgs e) {
        if (!IsVisible) {
            return;
        }

        if (_pauseHandler is null) {
            RefreshCore();
            return;
        }

        if (!_pauseHandler.IsPaused) {
            _hasRefreshedDuringCurrentPause = false;
            return;
        }

        if (_hasRefreshedDuringCurrentPause) {
            return;
        }

        _hasRefreshedDuringCurrentPause = true;
        RefreshCore();
    }

    private void OnResumed() {
        _hasRefreshedDuringCurrentPause = false;
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_isDisposed) {
            return;
        }
        _isDisposed = true;
        _refreshTimer.Stop();
        if (_pauseHandler is not null) {
            _pauseHandler.Resumed -= OnResumed;
        }
    }

    /// <summary>
    /// Updates the view-model state from the emulator state.
    /// </summary>
    protected abstract void RefreshCore();
}
