namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using Spice86.ViewModels;
using Spice86.ViewModels.Services;

/// <summary>
///     Code-behind for the MemoryBitmapView, managing the DispatcherTimer lifecycle
///     for periodic bitmap refresh.
/// </summary>
public partial class MemoryBitmapView : UserControl {
    private DispatcherTimer? _timer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MemoryBitmapView"/> class.
    /// </summary>
    public MemoryBitmapView() {
        InitializeComponent();
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        if (DataContext is IEmulatorObjectViewModel vm) {
            vm.IsVisible = false;
            _timer?.Stop();
            _timer = null;
        }
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        if (DataContext is IEmulatorObjectViewModel vm) {
            vm.IsVisible = true;
            _timer = DispatcherTimerStarter.StartNewDispatcherTimer(
                TimeSpan.FromMilliseconds(500),
                DispatcherPriority.Background,
                vm.UpdateValues);
        }
    }
}
