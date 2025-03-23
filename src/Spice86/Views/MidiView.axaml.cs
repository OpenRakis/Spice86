namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;

using Spice86.ViewModels;

public partial class MidiView : UserControl {
    public MidiView() {
        InitializeComponent();
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }


    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        if (DataContext is MidiViewModel vm) {
            vm.IsVisible = this.IsVisible;
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        if (DataContext is MidiViewModel vm) {
            vm.IsVisible = false;
        }
    }
}