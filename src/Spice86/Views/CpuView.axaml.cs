using Avalonia;
using Avalonia.Controls;

using Spice86.ViewModels;

namespace Spice86.Views;

public partial class CpuView : UserControl {
    public CpuView() {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        if (DataContext is CpuViewModel vm) {
            vm.IsVisible = this.IsVisible;
            this.DetachedFromVisualTree += OnDetachedFromVisualTree;
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        if (DataContext is CpuViewModel vm) {
            vm.IsVisible = false;
        }
    }
}