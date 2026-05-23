namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.VM;
using Spice86.ViewModels.ValueViewModels.Debugging;

/// <summary>
/// Base class for read-only inspector sub-tabs: one Info object rendered by a PropertyGrid, refreshed by a dispatcher timer while visible.
/// </summary>
/// <typeparam name="TInfo">The Info POCO type rendered by this view-model.</typeparam>
public abstract partial class InspectorViewModelBase<TInfo> : TimerRefreshViewModelBase
    where TInfo : InspectorInfoBase, new() {
    [ObservableProperty]
    private TInfo _info = new();

    /// <summary>
    /// Initializes the dispatcher timer that refreshes <see cref="Info"/> while the panel is visible.
    /// </summary>
    /// <param name="refreshIntervalMs">Polling period in milliseconds.</param>
    protected InspectorViewModelBase(int refreshIntervalMs) : base(refreshIntervalMs) {
    }

    /// <summary>
    /// Initializes the dispatcher timer that refreshes <see cref="Info"/> while the panel is visible,
    /// and applies once-per-pause refresh semantics.
    /// </summary>
    /// <param name="refreshIntervalMs">Polling period in milliseconds.</param>
    /// <param name="pauseHandler">Pause state source.</param>
    protected InspectorViewModelBase(int refreshIntervalMs, IPauseHandler pauseHandler) : base(refreshIntervalMs, pauseHandler) {
    }

    /// <inheritdoc />
    protected sealed override void RefreshCore() {
        RefreshInfo(Info);
    }

    /// <summary>
    /// Implementers copy current emulator state into <paramref name="info"/>.
    /// </summary>
    /// <param name="info">The Info instance to update in-place.</param>
    protected abstract void RefreshInfo(TInfo info);
}
