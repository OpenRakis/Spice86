namespace Spice86.Views;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

using System.ComponentModel;

using Spice86.ViewModels;

/// <summary>
/// Modern implementation of the disassembly view with improved performance and usability.
/// </summary>
public partial class ModernDisassemblyView : UserControl {
    private ListBox? _listBox;

    public ModernDisassemblyView() {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private ModernDisassemblyViewModel? ViewModel => DataContext as ModernDisassemblyViewModel;

    protected override void OnLoaded(RoutedEventArgs e) {
        base.OnLoaded(e);
        
        // Find the ListBox
        _listBox = this.FindControl<ListBox>("InstructionsListBox");
    }

    private void OnDataContextChanged(object? sender, EventArgs e) {
        if (DataContext is ModernDisassemblyViewModel viewModel) {
            // Unsubscribe from the old view model's property changed event if needed
            if (sender is ModernDisassemblyViewModel oldViewModel) {
                oldViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }
            
            // Subscribe to the new view model's property changed event
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    /// <summary>
    /// Handles pointer pressed events on instruction items in the disassembly view.
    /// </summary>
    /// <param name="sender">The sender of the event.</param>
    /// <param name="e">The event arguments.</param>
    private void Instruction_PointerPressed(object sender, PointerPressedEventArgs e) {
        if (sender is Control control && control.DataContext is DebuggerLineViewModel line && ViewModel != null) {
            // Select the line
            ViewModel.SelectedDebuggerLine = line;
            
            // Handle right click for context menu
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed) {
                // Show context menu
                // TODO: Implement context menu
            }
        }
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        // Ensure we're on the UI thread
        if (!Dispatcher.UIThread.CheckAccess()) {
            Dispatcher.UIThread.Post(() => ViewModel_PropertyChanged(sender, e));
            return;
        }

        if (e.PropertyName == nameof(ModernDisassemblyViewModel.CurrentInstructionAddress)) {
            Console.WriteLine($"CurrentInstructionAddress changed, not doing anything, just logging");
        }
    }
}