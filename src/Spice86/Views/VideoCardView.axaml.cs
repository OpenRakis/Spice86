using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

using Spice86.ViewModels;

using System.ComponentModel;

namespace Spice86.Views;

public partial class VideoCardView : UserControl {
    public VideoCardView() {
        InitializeComponent();
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        if (DataContext is VideoCardViewModel vm) {
            vm.IsVisible = false;
        }
    }

    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        if (DataContext is VideoCardViewModel vm) {
            vm.IsVisible = this.IsVisible;
        }
    }
}