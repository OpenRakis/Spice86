namespace Spice86.Views;

using Avalonia.Controls;

using Spice86.ViewModels;

/// <summary>
/// Dialog for creating execution breakpoints with optional conditions.
/// </summary>
public partial class BreakpointDialog : Window {
    /// <summary>
    /// Initializes a new instance of the <see cref="BreakpointDialog"/> class.
    /// </summary>
    public BreakpointDialog() {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e) {
        if (DataContext is BreakpointDialogViewModel viewModel) {
            // Subscribe to property changes to close the dialog when DialogResult changes
            viewModel.PropertyChanged += (s, args) => {
                if (args.PropertyName == nameof(BreakpointDialogViewModel.DialogResult)) {
                    Close(viewModel.DialogResult);
                }
            };
        }
    }
}
