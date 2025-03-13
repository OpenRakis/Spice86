namespace Spice86.Views;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.ComponentModel;
using Spice86.Behaviors;
using Spice86.ViewModels;

/// <summary>
/// View for the modern disassembly interface.
/// </summary>
public partial class ModernDisassemblyView : UserControl {
    private IModernDisassemblyViewModel? _viewModel;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="ModernDisassemblyView"/> class.
    /// </summary>
    public ModernDisassemblyView() {
        InitializeComponent();
        DataContextChanged += ModernDisassemblyView_DataContextChanged;
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }

    private void ModernDisassemblyView_DataContextChanged(object? sender, EventArgs e) {
        // Unsubscribe from the old view model if it exists
        if (_viewModel is INotifyPropertyChanged oldViewModel) {
            oldViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        // Subscribe to the new view model
        _viewModel = DataContext as IModernDisassemblyViewModel;
        if (_viewModel is INotifyPropertyChanged newViewModel) {
            newViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        // When the CurrentInstructionAddress property changes, scroll to that address
        if (e.PropertyName == nameof(IModernDisassemblyViewModel.CurrentInstructionAddress)) {
            ScrollToCurrentInstruction();
        }
    }

    private void ScrollToCurrentInstruction() {
        if (_viewModel == null) {
            return;
        }

        ListBox? disassemblyListBox = this.FindControl<ListBox>("DisassemblyListBox");
        if (disassemblyListBox == null) {
            return;
        }

        // Get the current instruction address from the view model
        uint currentInstructionAddress = _viewModel.CurrentInstructionAddress;
        
        // Scroll to the current instruction address
        DisassemblyScrollBehavior.ScrollToAddress(disassemblyListBox, currentInstructionAddress);
    }

    /// <summary>
    /// Event handler for the GoToCsIp button click.
    /// Directly calls the ScrollToAddress method in the DisassemblyScrollBehavior.
    /// </summary>
    private void GoToCsIpButton_Click(object? sender, RoutedEventArgs e) {
        ScrollToCurrentInstruction();
    }
}