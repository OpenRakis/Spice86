using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Spice86.Models.Debugging;
using Spice86.ViewModels;
using System.ComponentModel;

namespace Spice86.Views;

public partial class ModernDisassemblyView : UserControl {
    private ModernDisassemblyViewModel? _viewModel;
    private ScrollViewer? _scrollViewer;
    private ItemsRepeater? _itemsRepeater;

    public ModernDisassemblyView() {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        
        // We'll handle key events at the control level
        this.KeyDown += ModernDisassemblyView_KeyDown;
        
        // Handle mouse wheel for scrolling
        this.PointerWheelChanged += ModernDisassemblyView_PointerWheelChanged;
        
        // Get references to UI elements
        Loaded += ModernDisassemblyView_Loaded;
    }

    private void ModernDisassemblyView_Loaded(object? sender, RoutedEventArgs e) {
        // Get references to UI elements
        _scrollViewer = this.FindControl<ScrollViewer>("DisassemblyScrollViewer");
        _itemsRepeater = _scrollViewer?.FindControl<ItemsRepeater>("DisassemblyItemsRepeater");
        
        // Set up scroll to instruction handler
        if (_viewModel != null) {
            _viewModel.ScrollToAddress += ScrollToAddress;
        }
        
        // Initial scroll to current instruction after a short delay
        Dispatcher.UIThread.Post(() => ScrollToCurrentInstruction(), DispatcherPriority.Background);
    }
    
    private void ScrollToAddress(uint address) {
        if (_viewModel == null || _itemsRepeater == null || _scrollViewer == null) {
            return;
        }
        
        // Find the index of the instruction
        int index = GetIndex(_viewModel.DebuggerLines.Keys, address);
        if (index < 0) {
            return;
        }
        
        // Get the container for this item
        var container = _itemsRepeater.TryGetElement(index);
        if (container != null) {
            // Scroll to this container, centering it in the view
            // Calculate the position to scroll to (center the item in the viewport)
            var containerBounds = container.Bounds;
            var viewportHeight = _scrollViewer.Viewport.Height;
            
            // Calculate the target scroll position (center the item)
            var targetScrollPosition = containerBounds.Y - (viewportHeight / 2) + (containerBounds.Height / 2);
            
            // Ensure we don't scroll beyond bounds
            targetScrollPosition = Math.Max(0, targetScrollPosition);
            
            // Set the scroll position
            _scrollViewer.Offset = new Vector(0, targetScrollPosition);
        }
    }

    private void ScrollToCurrentInstruction()
    {
        if (_itemsRepeater == null || _scrollViewer == null || 
            _viewModel == null || _viewModel.DebuggerLines.Count == 0)
        {
            return;
        }

        // Find the index of the current instruction
        int currentIndex = GetIndex(_viewModel.DebuggerLines.Keys, _viewModel.CurrentlyFocusedAddress);
        if (currentIndex < 0) {
            Console.WriteLine($"Couldn't find line with address {_viewModel.CurrentlyFocusedAddress:X8}");
            return;
        }

        Console.WriteLine($"Found address {_viewModel.CurrentlyFocusedAddress:X8} at index {currentIndex}");

        // Use a multi-step approach to ensure the container is created
        TryScrollToCurrentInstruction(currentIndex, 0);
    }

    private int GetIndex(IEnumerable<uint> collection, uint target)
    {
        int index = 0;
        foreach (uint item in collection)
        {
            if (item == target)
                return index;
            index++;
        }
        return -1;
    }

    private async void TryScrollToCurrentInstruction(int index, int attemptCount)
    {
        if (_itemsRepeater == null || _scrollViewer == null || attemptCount > 5)
        {
            return;
        }

        // Try to get the container
        var container = _itemsRepeater.TryGetElement(index);
        
        if (container != null)
        {
            // We found the container, scroll to it
            try
            {
                // Calculate the position to center the item in the viewport
                var containerBounds = container.Bounds;
                var viewportHeight = _scrollViewer.Viewport.Height;
                
                // Calculate the target offset to center the item
                double targetOffset = containerBounds.Y - (viewportHeight / 2) + (containerBounds.Height / 2);
                targetOffset = Math.Max(0, targetOffset); // Ensure we don't scroll past the top
                
                // Scroll to the calculated position
                _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, targetOffset);
                
                Console.WriteLine($"Successfully scrolled to instruction at index {index}, offset {targetOffset}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scrolling to instruction: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine($"Container for instruction at index {index} not found, attempt {attemptCount + 1}");
            
            // If we can't find the container, try to scroll to approximately where it should be
            if (attemptCount == 0 && _viewModel?.DebuggerLines is {Count: > 0})
            {
                // Approximate position based on index
                double approximatePosition = (double)index / _viewModel.DebuggerLines.Count;
                double estimatedOffset = approximatePosition * _scrollViewer.Extent.Height;
                
                // Set an approximate scroll position to force container creation
                _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, estimatedOffset);
                Console.WriteLine($"Set approximate scroll position to {estimatedOffset}");
            }
            
            // Schedule another attempt with a delay
            await Task.Delay(100 * (attemptCount + 1)); // Increasing delay for each attempt
            
            // Make sure we're on the UI thread when we try again
            await Dispatcher.UIThread.InvokeAsync(() => TryScrollToCurrentInstruction(index, attemptCount + 1));
        }
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnDataContextChanged(object? sender, EventArgs e) {
        // Unsubscribe from old view model
        if (_viewModel != null) {
            _viewModel.ScrollToAddress -= ScrollToAddress;
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }
        
        // Set new view model
        _viewModel = DataContext as ModernDisassemblyViewModel;
        
        // Subscribe to new view model
        if (_viewModel != null) {
            _viewModel.ScrollToAddress += ScrollToAddress;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Properties that should trigger scrolling to the current instruction
        if (e.PropertyName is nameof(ModernDisassemblyViewModel.CurrentlyFocusedAddress))
        {
            // When instructions are updated or execution pauses, scroll to the current instruction
            Dispatcher.UIThread.Post(ScrollToCurrentInstruction, DispatcherPriority.Background);
        }
    }

    private void Instruction_PointerPressed(object sender, PointerPressedEventArgs e) {
        if (_viewModel == null) {
            return;
        }

        // Get the instruction from the sender's DataContext
        if (sender is Border {DataContext: EnrichedInstruction instruction}) {
            // Set the selected instruction in the view model
            _viewModel.SelectedInstruction = instruction;
            
            // Handle double-click for creating breakpoint
            if (e.ClickCount == 2) {
                _viewModel.CreateExecutionBreakpointHereCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void ModernDisassemblyView_PointerWheelChanged(object? sender, PointerWheelEventArgs e) {
        if (_viewModel == null || !_viewModel.IsPaused) {
            return;
        }
        
        // Handle mouse wheel scrolling
        if (e.Delta.Y > 0) {
            // Scroll up
            _viewModel.LineUpCommand.Execute(null);
        } else if (e.Delta.Y < 0) {
            // Scroll down
            _viewModel.LineDownCommand.Execute(null);
        }
        
        e.Handled = true;
    }

    private void ModernDisassemblyView_KeyDown(object? sender, KeyEventArgs e) {
        if (DataContext is ModernDisassemblyViewModel viewModel) {
            // Handle keyboard shortcuts
            if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control &&
                viewModel.CopyLineCommand.CanExecute(null)) {
                viewModel.CopyLineCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.F2 &&
                viewModel.CreateExecutionBreakpointHereCommand.CanExecute(null)) {
                viewModel.CreateExecutionBreakpointHereCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete &&
                viewModel.RemoveExecutionBreakpointHereCommand.CanExecute(null)) {
                viewModel.RemoveExecutionBreakpointHereCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && 
                viewModel.UpdateDisassemblyCommand.CanExecute(null)) {
                viewModel.UpdateDisassemblyCommand.Execute(null);
                e.Handled = true;
            }
            // Navigation keys
            else if (e.Key == Key.Up && viewModel.LineUpCommand.CanExecute(null)) {
                viewModel.LineUpCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Down && viewModel.LineDownCommand.CanExecute(null)) {
                viewModel.LineDownCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.PageUp && viewModel.PageUpCommand.CanExecute(null)) {
                viewModel.PageUpCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.PageDown && viewModel.PageDownCommand.CanExecute(null)) {
                viewModel.PageDownCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Home && viewModel.GoToCsIpCommand.CanExecute(null)) {
                viewModel.GoToCsIpCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
