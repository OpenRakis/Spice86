namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;

using Spice86.ViewModels;

internal partial class PaletteView : UserControl {
    public PaletteView() {
        InitializeComponent();
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        if (DataContext is PaletteViewModel vm) {
            vm.IsVisible = false;
        }
    }

    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        if (DataContext is PaletteViewModel vm) {
            vm.IsVisible = this.IsVisible;
        }
    }
}