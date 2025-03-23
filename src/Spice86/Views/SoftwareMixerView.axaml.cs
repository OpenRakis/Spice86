namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;

using Spice86.ViewModels;

internal sealed partial class SoftwareMixerView : UserControl {
    public SoftwareMixerView() {
        InitializeComponent();
        DetachedFromVisualTree += OnDetachedFromVisualTree;

    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        if (DataContext is SoftwareMixerViewModel vm) {
            vm.IsVisible = false;
        }
    }

    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        if (DataContext is SoftwareMixerViewModel vm) {
            vm.IsVisible = this.IsVisible;
        }
    }
}