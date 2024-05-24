namespace Spice86.Views;

using Avalonia.Controls;

using Spice86.ViewModels;

public sealed partial class DebugWindow : Window {
    public DebugWindow() {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        ((DebugWindowViewModel?)DataContext)?.StartObserverTimer();
    }
}