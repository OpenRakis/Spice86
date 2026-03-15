namespace Spice86.Views;

using Avalonia.Controls;

using Spice86.ViewModels;

public sealed partial class HttpApiWindow : Window {
    public HttpApiWindow() {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e) {
        base.OnOpened(e);
        if (DataContext is HttpApiViewModel viewModel && viewModel.RefreshStatusCommand.CanExecute(null)) {
            viewModel.RefreshStatusCommand.Execute(null);
        }
    }
}
