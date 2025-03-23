namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using Spice86.Infrastructure;

using Spice86.ViewModels;

internal sealed partial class SoftwareMixerView : UserControl {
    private DispatcherTimer? _timer;
    public SoftwareMixerView() {
        InitializeComponent();
        this.DetachedFromVisualTree += OnDetachedFromVisualTree;
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
            vm.IsVisible = this.IsVisible;
            _timer = DispatcherTimerStarter.StartNewDispatcherTimer(
                TimeSpan.FromMilliseconds(400), DispatcherPriority.Background,
                vm.UpdateValues);
        }
    }
}