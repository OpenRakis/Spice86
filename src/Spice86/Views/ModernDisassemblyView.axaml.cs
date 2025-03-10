namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

using System;
using System.ComponentModel;
using System.Linq;

using Spice86.ViewModels;

/// <summary>
/// Modern implementation of the disassembly view with improved performance and usability.
/// </summary>
public partial class ModernDisassemblyView : UserControl {
    private ListBox? _listBox;
    private ScrollViewer? _scrollViewer;
    private double _estimatedItemHeight = 20; // Default estimate

    public ModernDisassemblyView() {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private ModernDisassemblyViewModel? ViewModel => DataContext as ModernDisassemblyViewModel;

    protected override void OnLoaded(RoutedEventArgs e) {
        base.OnLoaded(e);

        // Find the ListBox
        _listBox = this.FindControl<ListBox>("DisassemblyListBox");

        // Get the ScrollViewer from the ListBox
        if (_listBox != null) {
            _scrollViewer = _listBox.FindDescendantOfType<ScrollViewer>();
        }
        Console.WriteLine("View loaded");
        // Scroll to the item with the current address
        if (ViewModel != null) {
            ScrollToAddress(ViewModel.CurrentInstructionAddress);
        }
    }

    private void UpdateItemHeightEstimate() {
        // Try to find a visible item to measure
        ListBoxItem? visibleItem = _listBox?.GetVisualDescendants().OfType<ListBoxItem>().FirstOrDefault();

        if (visibleItem != null) {
            _estimatedItemHeight = visibleItem.Bounds.Height;
            Console.WriteLine($"Updated estimated item height to {_estimatedItemHeight}");
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
        }
    }

    /// <summary>
    /// Handles pointer pressed events on instruction items in the disassembly view.
    /// </summary>
    /// <param name="sender">The sender of the event.</param>
    /// <param name="e">The event arguments.</param>
    private void Instruction_PointerPressed(object sender, PointerPressedEventArgs e) {
        if (sender is Control {DataContext: DebuggerLineViewModel line} && ViewModel != null) {
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
        Console.WriteLine($"ViewModel_PropertyChanged({sender?.GetType().Name}, {e.PropertyName})");
        // Ensure we're on the UI thread
        if (!Dispatcher.UIThread.CheckAccess()) {
            Dispatcher.UIThread.Post(() => ViewModel_PropertyChanged(sender, e));

            return;
        }

        if (ViewModel == null) {
            return;
        }

        if (e.PropertyName == nameof(ModernDisassemblyViewModel.CurrentInstructionAddress)) {
            Console.WriteLine($"CurrentInstructionAddress changed to {ViewModel?.CurrentInstructionAddress:X8}, scrolling to item");

            // Scroll to the item with the matching address
            if (ViewModel != null) {
                uint instructionAddress = ViewModel.CurrentInstructionAddress;
                if (!IsWithinMiddleRangeOfViewPort(instructionAddress)) {
                    ScrollToAddress(instructionAddress);
                }
            }
        }
    }

    private List<DebuggerLineViewModel> GetVisibleDebuggerLines()
    {
        if (_listBox == null || _scrollViewer == null || ViewModel == null)
        {
            Console.WriteLine("ListBox, ScrollViewer, or ViewModel is null");
            return [];
        }

        var visibleItems = _listBox.GetVisualDescendants()
            .OfType<ListBoxItem>()
            .Where(item => item.DataContext is DebuggerLineViewModel)
            .Select(item => (DebuggerLineViewModel)item.DataContext!)
            .ToList();

        return visibleItems;
    }

    private static List<DebuggerLineViewModel> GetMiddleItems(List<DebuggerLineViewModel> items, int excludeFromTopAndBottom = 3)
    {
        if (items.Count <= excludeFromTopAndBottom * 3)
        {
            // Return all items if there aren't enough items to exclude from top and bottom
            return items;
        }

        // Return the middle items, excluding the specified number from top and bottom
        return items.Skip(excludeFromTopAndBottom).Take(items.Count - (excludeFromTopAndBottom * 2)).ToList();
    }

    private bool IsWithinMiddleRangeOfViewPort(uint address) {
        List<DebuggerLineViewModel> visibleItems = GetVisibleDebuggerLines();

        // Get the middle items, excluding top and bottom 3
        List<DebuggerLineViewModel> middleItems = GetMiddleItems(visibleItems);

        // Check if any of the middle items has the target address
        return middleItems.Any(item => item.Address == address);
    }

    /// <summary>
    /// Scrolls the list box to center the current instruction in the viewport.
    /// </summary>
    /// <param name="targetAddress"></param>
    private void ScrollToAddress(long targetAddress) {
        if (_listBox == null || _scrollViewer == null || ViewModel == null) {
            Console.WriteLine("ListBox, ScrollViewer, or ViewModel is null");

            return;
        }

        // Find the item with the matching address
        var items = _listBox.ItemsSource?.Cast<DebuggerLineViewModel>().ToList();
        if (items == null || items.Count == 0) {
            Console.WriteLine("No items in the ListBox");

            return;
        }
        Console.WriteLine($"Looking for item with address {targetAddress:X8} among {items.Count} items");
        var targetItem = items.FirstOrDefault(item => item.Address == targetAddress);
        if (targetItem == null) {
            Console.WriteLine($"Could not find instruction with address {targetAddress:X8} in the list");

            return;
        }
        Console.WriteLine($"Found target item with address {targetItem.Address:X8}");

        // First make sure the item is in view
        _listBox.ScrollIntoView(targetItem);

        // Update our item height estimate if possible
        UpdateItemHeightEstimate();

        // Force layout update and then center the item
        Dispatcher.UIThread.Post(() => {
            try {
                // Try to find the actual container for more accurate positioning
                ListBoxItem? container = _listBox.GetVisualDescendants().OfType<ListBoxItem>().FirstOrDefault(item => item.DataContext is DebuggerLineViewModel line && line.Address == targetAddress);

                double actualOffset;

                if (container == null) {
                    Console.WriteLine("Container not found");
                    // Fall back to index-based calculation
                    // Get the index of the target item
                    int targetIndex = items.IndexOf(targetItem);
                    if (targetIndex < 0) {
                        Console.WriteLine("Could not determine index of target item");

                        return;
                    }
                    Console.WriteLine($"Target item is at index {targetIndex} of {items.Count}");
                    actualOffset = CalculateOffsetByIndex(targetIndex, items.Count);
                } else {
                    // We found the container, so we can get its actual position
                    Point? itemPosition = container.TranslatePoint(new Point(0, 0), _scrollViewer);
                    if (itemPosition.HasValue) {
                        double itemHeight = container.Bounds.Height;
                        double viewportHeight = _scrollViewer.Viewport.Height;

                        // Calculate the offset needed to center this specific item
                        actualOffset = _scrollViewer.Offset.Y + itemPosition.Value.Y - (viewportHeight / 2) + (itemHeight / 2);
                        Console.WriteLine($"Using actual container position: {itemPosition.Value.Y}, height: {itemHeight}");
                    } else {
                        // Fall back to index-based calculation
                        // Get the index of the target item
                        int targetIndex = items.IndexOf(targetItem);
                        if (targetIndex < 0) {
                            Console.WriteLine("Could not determine index of target item");

                            return;
                        }
                        Console.WriteLine($"Target item is at index {targetIndex} of {items.Count}");
                        actualOffset = CalculateOffsetByIndex(targetIndex, items.Count);
                    }
                }

                // Get current scroll position for comparison
                double currentOffset = _scrollViewer.Offset.Y;
                Console.WriteLine($"Current offset: {currentOffset}, Calculated offset: {actualOffset}");

                // Apply the new scroll position
                _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, Math.Max(0, actualOffset));

                // Verify the scroll position changed
                Dispatcher.UIThread.Post(() => {
                    double newPosition = _scrollViewer.Offset.Y;
                    Console.WriteLine($"Scroll position after centering: {newPosition}");

                    if (Math.Abs(newPosition - actualOffset) > 0.1) {
                        Console.WriteLine("WARNING: Scroll position doesn't match calculated offset!");

                        // Try one more time with a direct approach
                        _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, Math.Max(0, actualOffset));

                        Dispatcher.UIThread.Post(() => {
                            Console.WriteLine($"Final scroll position: {_scrollViewer.Offset.Y}");
                        }, DispatcherPriority.Render);
                    } else {
                        Console.WriteLine($"Successfully centered instruction at address {targetAddress:X8}");
                    }
                }, DispatcherPriority.Render);
            } catch (Exception ex) {
                Console.WriteLine($"Error while centering item: {ex.Message}");
            }
        }, DispatcherPriority.Render);
    }

    private double CalculateOffsetByIndex(int targetIndex, int totalItems) {
        // Calculate position based on index and estimated item height
        double totalContentHeight = totalItems * _estimatedItemHeight;
        double viewportHeight = _scrollViewer!.Viewport.Height;

        Console.WriteLine($"Using estimated item height: {_estimatedItemHeight}");
        Console.WriteLine($"Content height: {totalContentHeight}, Viewport height: {viewportHeight}");

        // Calculate the position that would center the item
        double targetPosition = (targetIndex * _estimatedItemHeight);
        double centeringOffset = targetPosition - (viewportHeight / 2) + (_estimatedItemHeight / 2);

        // Ensure we don't scroll beyond bounds
        double maxScroll = Math.Max(0, totalContentHeight - viewportHeight);
        double newOffset = Math.Max(0, Math.Min(centeringOffset, maxScroll));

        Console.WriteLine($"Calculated offset by index: {newOffset}");

        return newOffset;
    }
}