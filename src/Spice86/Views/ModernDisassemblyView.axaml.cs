namespace Spice86.Views;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

using CommunityToolkit.Mvvm.Messaging;

using System.ComponentModel;

using Spice86.ViewModels;
using Spice86.Models.Debugging;

/// <summary>
/// Modern implementation of the disassembly view with improved performance and usability.
/// </summary>
public partial class ModernDisassemblyView : UserControl {
    private ListBox? _listBox;
    private ScrollViewer? _scrollViewer;
    private uint _lastScrolledAddress;
    
    // Flag to prevent recursive scrolling
    private bool _isScrolling;

    public ModernDisassemblyView() {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private ModernDisassemblyViewModel? ViewModel => DataContext as ModernDisassemblyViewModel;

    protected override void OnLoaded(RoutedEventArgs e) {
        base.OnLoaded(e);
        
        // Find the ListBox and ScrollViewer
        _listBox = this.FindControl<ListBox>("InstructionsListBox");
        if (_listBox != null) {
            _scrollViewer = _listBox.FindDescendantOfType<ScrollViewer>();
        }
        
        // Subscribe to the ScrollToAddress event
        if (ViewModel != null) {
            ViewModel.ScrollToAddress += ScrollToAddress;
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e) {
        base.OnUnloaded(e);
        
        // Unsubscribe from the ScrollToAddress event
        if (ViewModel != null) {
            ViewModel.ScrollToAddress -= ScrollToAddress;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e) {
        if (DataContext is ModernDisassemblyViewModel viewModel) {
            // Unsubscribe from the old view model's property changed event if needed
            if (sender is ModernDisassemblyViewModel oldViewModel) {
                oldViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }
            
            // Subscribe to the new view model's property changed event
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
            
            // Subscribe to the ScrollToAddress event
            viewModel.ScrollToAddress += ScrollToAddress;
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

    /// <summary>
    /// Scrolls to the specified address in the disassembly view.
    /// </summary>
    /// <param name="address">The address to scroll to.</param>
    private void ScrollToAddress(uint address) {
        // Ensure we're on the UI thread
        if (!Dispatcher.UIThread.CheckAccess()) {
            Dispatcher.UIThread.Post(() => ScrollToAddress(address));
            return;
        }
        
        // Prevent recursive scrolling
        if (_isScrolling) {
            return;
        }
        
        _isScrolling = true;
        
        try {
            // Skip if we've already scrolled to this address
            if (address == _lastScrolledAddress) {
                return;
            }
            
            // Find the line with the specified address
            if (ViewModel?.DebuggerLines.TryGetValue(address, out DebuggerLineViewModel? line) == true) {
                // Scroll with the line in the middle 50% of the visible area
                ScrollToLineWithMiddlePlacement(line);
                
                // Remember this address to avoid duplicate scrolling
                _lastScrolledAddress = address;
                
                Console.WriteLine($"Scrolled to address {address:X8}");
            } else {
                Console.WriteLine($"Address {address:X8} not found in the disassembly");
            }
        } finally {
            _isScrolling = false;
        }
    }

    /// <summary>
    /// Scrolls to the current instruction in the disassembly view.
    /// </summary>
    private void ScrollToCurrentInstruction() {
        if (ViewModel == null) {
            return;
        }

        uint address = ViewModel.CurrentInstructionAddress;
        if (ViewModel.DebuggerLines.TryGetValue(address, out DebuggerLineViewModel? line)) {
            ScrollToLineWithMiddlePlacement(line);
        }
    }

    /// <summary>
    /// Scrolls to the specified line, ensuring it's in the middle 50% of the visible area.
    /// </summary>
    /// <param name="line">The line to scroll to.</param>
    /// <param name="retryCount">The number of retries attempted so far.</param>
    private void ScrollToLineWithMiddlePlacement(DebuggerLineViewModel line, int retryCount = 0) {
        // Ensure we're on the UI thread
        if (!Dispatcher.UIThread.CheckAccess()) {
            Dispatcher.UIThread.Post(() => ScrollToLineWithMiddlePlacement(line, retryCount));
            return;
        }

        if (_listBox == null) {
            return;
        }

        // First, bring the item into view to ensure it's in the virtualized panel
        _listBox.ScrollIntoView(line);

        Dispatcher.UIThread.Post(() => {
            if (_listBox == null || _scrollViewer == null || _listBox.ItemsSource == null) {
                return;
            }
            
            // Find the index of the item in the ItemsSource
            int itemIndex = -1;
            var itemsList = _listBox.ItemsSource.Cast<object>().ToList();
            for (int i = 0; i < itemsList.Count; i++) {
                if (itemsList[i] == line) {
                    itemIndex = i;
                    break;
                }
            }
            
            if (itemIndex == -1) {
                Console.WriteLine($"Item with address {line.Address:X8} not found in the list");
                return;
            }
            
            // Find the container for the item
            if (_listBox.ContainerFromIndex(itemIndex) is not { } container) {
                // If container is not found, try scrolling into view again and retry
                _listBox.ScrollIntoView(line);
                Console.WriteLine($"Container for line at address {line.Address:X8} not found, retrying ({retryCount + 1})...");
                
                if (retryCount < 5) { // Limit to 5 retries
                    // Schedule another attempt after a delay that increases with each retry
                    Dispatcher.UIThread.Post(() => ScrollToLineWithMiddlePlacement(line, retryCount + 1), DispatcherPriority.Background);
                } else {
                    Console.WriteLine($"Gave up trying to scroll to line at address {line.Address:X8} after {retryCount} retries");
                }
                return;
            }
            
            // Get the viewport height
            double viewportHeight = _scrollViewer.Viewport.Height;
            
            // Get the item's position in the list
            double itemPosition = itemIndex * container.Bounds.Height;
            
            // Calculate the target offset to center the item in the viewport
            double targetOffset = itemPosition - (viewportHeight / 2) + (container.Bounds.Height / 2);
            
            // Ensure the offset is within valid bounds
            targetOffset = Math.Max(0, Math.Min(targetOffset, _scrollViewer.Extent.Height - viewportHeight));
            
            // Set the new scroll position
            _scrollViewer.Offset = new Avalonia.Vector(
                _scrollViewer.Offset.X,
                targetOffset
            );
            
            // Log the action
            Console.WriteLine($"Centered line at address {line.Address:X8} in the viewport (offset: {targetOffset})");
        }, retryCount == 0 ? DispatcherPriority.Render : DispatcherPriority.Background);
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
            // When CurrentInstructionAddress changes, update the highlighting and scroll to the instruction
            // This typically happens when the emulator pauses
            Console.WriteLine($"CurrentInstructionAddress changed, updating highlighting and scrolling");
            ScrollToCurrentInstruction();
        }
    }
}