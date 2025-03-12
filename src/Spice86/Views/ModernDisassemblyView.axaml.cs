namespace Spice86.Views;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Spice86.Behaviors;
using Spice86.ViewModels;

/// <summary>
/// View for the modern disassembly interface.
/// </summary>
public partial class ModernDisassemblyView : UserControl {
    /// <summary>
    /// Initializes a new instance of the <see cref="ModernDisassemblyView"/> class.
    /// </summary>
    public ModernDisassemblyView() {
        InitializeComponent();
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }
    
    /// <summary>
    /// Event handler for the GoToCsIp button click.
    /// Directly calls the ScrollToAddress method in the DisassemblyScrollBehavior.
    /// </summary>
    private void GoToCsIpButton_Click(object? sender, RoutedEventArgs e) {
        if (DataContext is not IModernDisassemblyViewModel viewModel) {
            return;
        }
        
        ListBox? disassemblyListBox = this.FindControl<ListBox>("DisassemblyListBox");
        if (disassemblyListBox == null) {
            return;
        }
        
        // Get the current instruction address from the view model
        uint currentInstructionAddress = viewModel.CurrentInstructionAddress;
        
        // Directly call the ScrollToAddress method to ensure scrolling happens
        // regardless of whether the address has changed
        DisassemblyScrollBehavior.ScrollToAddress(disassemblyListBox, currentInstructionAddress);
    }
}