namespace Spice86.Views;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

using System.ComponentModel;

using Spice86.ViewModels;

/// <summary>
/// View for the disassembly interface.
/// </summary>
public partial class DisassemblyView : UserControl {
    private IDisassemblyViewModel? _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="DisassemblyView"/> class.
    /// </summary>
    public DisassemblyView() {
        InitializeComponent();
        DataContextChanged += DisassemblyView_DataContextChanged;
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }

    private void DisassemblyView_DataContextChanged(object? sender, EventArgs e) {
        // Unsubscribe from the old view model if it exists
        if (_viewModel != null) {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        // Subscribe to the new view model
        _viewModel = DataContext as IDisassemblyViewModel;
        if (_viewModel != null) {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private static void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
    }

    private void OnBreakpointClicked(object? sender, TappedEventArgs e) {
        if (sender is Control {DataContext: DebuggerLineViewModel debuggerLine}) {
            _viewModel?.ToggleBreakpointCommand.Execute(debuggerLine);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Event handler for when the function selection AutoCompleteBox gets focus.
    /// </summary>
    /// <param name="sender">The sender object.</param>
    /// <param name="e">The focus event arguments.</param>
    private void OnFunctionSelectionFocus(object? sender, GotFocusEventArgs e) {
        if (sender is AutoCompleteBox autoCompleteBox) {
            // Clear the text when the AutoCompleteBox gets focus
            autoCompleteBox.Text = string.Empty;
        }
    }

    private void OnFunctionSelectionKeyDown(object? sender, KeyEventArgs e) {
        if (e.Key == Key.Enter && DataContext is IDisassemblyViewModel viewModel) {
            // Execute the "Go to Function" command when Enter is pressed
            viewModel.GoToFunctionCommand.Execute(viewModel.SelectedFunction);
            e.Handled = true;
        }
    }
}