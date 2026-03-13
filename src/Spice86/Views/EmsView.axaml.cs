namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using Spice86.ViewModels;
using Spice86.ViewModels.Services;

public partial class EmsView : UserControl {
    private DispatcherTimer? _timer;

    public EmsView() {
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

    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        if (DataContext is IEmulatorObjectViewModel vm) {
            vm.IsVisible = IsVisible;
            _timer = DispatcherTimerStarter.StartNewDispatcherTimer(
                TimeSpan.FromMilliseconds(400), DispatcherPriority.Background,
                vm.UpdateValues);
        }
    }
}
