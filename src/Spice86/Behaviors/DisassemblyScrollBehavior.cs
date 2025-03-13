namespace Spice86.Behaviors;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;

using Spice86.ViewModels;

/// <summary>
/// Attached behavior for handling scrolling in the disassembly view.
/// This behavior encapsulates the UI-specific scrolling logic to improve separation of concerns.
/// </summary>
public class DisassemblyScrollBehavior
{
    // Attached property for enabling the behavior
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<DisassemblyScrollBehavior, Control, bool>("IsEnabled");

    // Attached property for the target address
    public static readonly AttachedProperty<uint> TargetAddressProperty =
        AvaloniaProperty.RegisterAttached<DisassemblyScrollBehavior, Control, uint>("TargetAddress");

    // Helper methods for getting/setting the attached properties
    public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(Control control, bool value) => control.SetValue(IsEnabledProperty, value);

    public static uint GetTargetAddress(Control control) => control.GetValue(TargetAddressProperty);
    public static void SetTargetAddress(Control control, uint value) => control.SetValue(TargetAddressProperty, value);

    // Static constructor to register property changed handlers
    static DisassemblyScrollBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
        TargetAddressProperty.Changed.AddClassHandler<Control>(OnTargetAddressChanged);
    }

    // Handler for when IsEnabled property changes
    private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (control is not ListBox listBox)
        {
            return;
        }

        bool isEnabled = (bool)e.NewValue!;
        if (isEnabled)
        {
            // Initialize any resources needed
            Console.WriteLine("DisassemblyScrollBehavior enabled");
            
            // Subscribe to the Loaded event to handle initial centering
            listBox.Loaded += OnListBoxLoaded;
            
            // Subscribe to the LayoutUpdated event to handle container generation
            listBox.LayoutUpdated += OnListBoxLayoutUpdated;
            
            // If the ListBox is already loaded, check if we need to scroll
            if (listBox.IsLoaded)
            {
                uint targetAddress = GetTargetAddress(listBox);
                if (targetAddress != 0)
                {
                    // Delay the scroll to ensure the UI has fully loaded
                    Dispatcher.UIThread.Post(() => ScrollToAddress(listBox, targetAddress), DispatcherPriority.Loaded);
                }
            }
        }
        else
        {
            // Clean up any resources
            Console.WriteLine("DisassemblyScrollBehavior disabled");
            listBox.Loaded -= OnListBoxLoaded;
            listBox.LayoutUpdated -= OnListBoxLayoutUpdated;
        }
    }

    // Handler for ListBox Loaded event
    private static void OnListBoxLoaded(object? sender, EventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }
        
        // Unsubscribe from the Loaded event to avoid multiple calls
        listBox.Loaded -= OnListBoxLoaded;
        
        // Check if we have a pending target for this ListBox
        if (_pendingScrollTargets.TryGetValue(listBox, out uint targetAddress) && targetAddress != 0)
        {
            // Now that the ListBox is loaded, we can scroll to the target address
            Console.WriteLine($"ListBox now loaded, scrolling to pending target {targetAddress:X8}");
            // Delay the scroll to ensure the UI has fully loaded
            Dispatcher.UIThread.Post(() => ScrollToAddress(listBox, targetAddress), DispatcherPriority.Render);
        }
        else
        {
            // Fallback to the attached property if no pending target
            targetAddress = GetTargetAddress(listBox);
            if (targetAddress != 0)
            {
                // Delay the scroll to ensure the UI has fully loaded
                Dispatcher.UIThread.Post(() => ScrollToAddress(listBox, targetAddress), DispatcherPriority.Render);
            }
        }
    }
    
    // Static field to track if we're currently processing a scroll operation
    private static bool _isScrollingInProgress;
    
    // Track the pending scroll target address for deferred scrolling
    private static Dictionary<ListBox, uint> _pendingScrollTargets = new Dictionary<ListBox, uint>();
    
    // Handler for ListBox LayoutUpdated event
    private static void OnListBoxLayoutUpdated(object? sender, EventArgs e)
    {
        if (sender is not ListBox listBox || _isScrollingInProgress)
        {
            return;
        }
        
        uint targetAddress = GetTargetAddress(listBox);
        if (targetAddress == 0)
        {
            return;
        }
        
        // Only proceed if we have a valid data context and target item
        if (listBox.DataContext is IModernDisassemblyViewModel viewModel)
        {
            DebuggerLineViewModel? targetItem = viewModel.GetLineByAddress(targetAddress);
            if (targetItem == null)
            {
                return;
            }
            
            // Try to find the container for the target item
            if (listBox.ContainerFromItem(targetItem) is ListBoxItem container)
            {
                // Container found, center it in the viewport
                ScrollViewer? scrollViewer = listBox.FindDescendantOfType<ScrollViewer>();
                if (scrollViewer != null)
                {
                    // We found both the container and scrollviewer, so we can center the item
                    DateTime startTime = DateTime.Now;
                    
                    // Set flag to prevent re-entrancy
                    _isScrollingInProgress = true;
                    
                    try
                    {
                        // Directly center the container without ScrollIntoView to avoid double scrolling
                        CenterContainerInViewport(scrollViewer, container, startTime);
                        
                        // Remove from pending targets if it was there
                        if (_pendingScrollTargets.ContainsKey(listBox))
                        {
                            _pendingScrollTargets.Remove(listBox);
                        }
                    }
                    finally
                    {
                        // Reset flag
                        _isScrollingInProgress = false;
                        
                        // Keep the LayoutUpdated handler if we still have pending targets
                        if (!_pendingScrollTargets.ContainsKey(listBox))
                        {
                            // Unsubscribe from the LayoutUpdated event to avoid unnecessary processing
                            listBox.LayoutUpdated -= OnListBoxLayoutUpdated;
                        }
                    }
                }
            }
            else
            {
                // Container not found yet, make sure this address is in our pending targets
                if (!_pendingScrollTargets.ContainsKey(listBox) || _pendingScrollTargets[listBox] != targetAddress)
                {
                    _pendingScrollTargets[listBox] = targetAddress;
                    
                    // Force a ScrollIntoView to trigger container creation
                    listBox.ScrollIntoView(targetItem);
                    listBox.InvalidateArrange();
                }
            }
        }
    }

    // Handler for when TargetAddress property changes
    private static void OnTargetAddressChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (control is not ListBox listBox || !GetIsEnabled(listBox))
        {
            return;
        }

        uint targetAddress = (uint)e.NewValue!;
        
        // Add to pending targets
        _pendingScrollTargets[listBox] = targetAddress;
        
        // Ensure we're subscribed to layout events for this new target address
        listBox.LayoutUpdated -= OnListBoxLayoutUpdated; // Remove any existing handler
        listBox.LayoutUpdated += OnListBoxLayoutUpdated;  // Add a fresh handler
        
        // Only scroll if the ListBox is loaded and visible
        if (listBox is {IsLoaded: true, IsVisible: true})
        {
            ScrollToAddress(listBox, targetAddress);
        }
        else
        {
            // If the ListBox is not loaded yet, we'll handle it in the Loaded event
            Console.WriteLine($"ListBox not loaded yet, will scroll to {targetAddress:X8} when loaded");
            
            // Subscribe to Loaded event if not already subscribed
            listBox.Loaded -= OnListBoxLoaded;
            listBox.Loaded += OnListBoxLoaded;
        }
    }

    // Main scrolling logic with timing information
    public static void ScrollToAddress(ListBox listBox, uint targetAddress)
    {
        // Ensure we're on the UI thread
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ScrollToAddress(listBox, targetAddress));
            return;
        }
        
        // Prevent re-entrancy
        if (_isScrollingInProgress)
        {
            return;
        }

        _isScrollingInProgress = true;
        try
        {
            DateTime startTime = DateTime.Now;
            Console.WriteLine($"[{startTime:HH:mm:ss.fff}] Starting scroll to address {targetAddress:X8}");

            // Find the ScrollViewer - first try to get it directly from the template
            // If not found, try to get it from the visual tree
            ScrollViewer? scrollViewer = listBox.FindDescendantOfType<ScrollViewer>() ?? listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

            if (scrollViewer == null)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ScrollViewer not found, elapsed: {(DateTime.Now - startTime).TotalMilliseconds}ms");
                return;
            }

            // Use the view model's GetLineByAddress method for efficient lookup
            if (listBox.DataContext is IModernDisassemblyViewModel viewModel)
            {
                DebuggerLineViewModel? targetItem = viewModel.GetLineByAddress(targetAddress);
                if (targetItem == null)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Could not find instruction with address {targetAddress:X8} in the dictionary, elapsed: {(DateTime.Now - startTime).TotalMilliseconds}ms");
                    return;
                }

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Found target item with address {targetItem.Address:X8}, elapsed: {(DateTime.Now - startTime).TotalMilliseconds}ms");
                
                // Add to pending targets to ensure we keep trying until it's visible
                _pendingScrollTargets[listBox] = targetAddress;
                
                // First, scroll the item into view to ensure the container is created
                listBox.ScrollIntoView(targetItem);
                
                // Force layout update to ensure ScrollIntoView is applied
                listBox.InvalidateArrange();
                
                // Try to find the container for the target item
                if (listBox.ContainerFromItem(targetItem) is ListBoxItem container)
                {
                    // Container found, center it in the viewport directly
                    CenterContainerInViewport(scrollViewer, container, startTime);
                    
                    // Successfully scrolled, remove from pending targets
                    _pendingScrollTargets.Remove(listBox);
                }
                else
                {
                    // Container not found yet, the LayoutUpdated event will handle it
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Container not found yet, will be handled when layout is updated, elapsed: {(DateTime.Now - startTime).TotalMilliseconds}ms");
                    
                    // Make sure we're subscribed to the LayoutUpdated event
                    listBox.LayoutUpdated -= OnListBoxLayoutUpdated; // Avoid duplicate handlers
                    listBox.LayoutUpdated += OnListBoxLayoutUpdated;
                    
                    // For jumps to addresses far outside the current viewport, we need to force a more aggressive scroll
                    // This helps ensure the virtualization panel creates the containers we need
                    Dispatcher.UIThread.Post(() => {
                        // Try again with a delay to allow the UI to update
                        listBox.ScrollIntoView(targetItem);
                        listBox.InvalidateArrange();
                    }, DispatcherPriority.Render);
                }
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] DataContext is not IModernDisassemblyViewModel, elapsed: {(DateTime.Now - startTime).TotalMilliseconds}ms");
            }
        }
        finally
        {
            _isScrollingInProgress = false;
        }
    }
    
    // Method to center a container in the viewport
    private static void CenterContainerInViewport(ScrollViewer scrollViewer, ListBoxItem container, DateTime startTime)
    {
        try
        {
            // Get the position and dimensions of the container
            Point itemPosition = container.TranslatePoint(new Point(0, 0), scrollViewer) ?? new Point(0, 0);
            double itemTop = itemPosition.Y;
            double itemHeight = container.Bounds.Height;
            double viewportHeight = scrollViewer.Viewport.Height;
            double itemBottom = itemTop + itemHeight;
            
            // Check if the item is already in the middle range of the viewport
            bool isInMiddleRange = IsItemInMiddleRange(itemTop, itemBottom, viewportHeight);
            if (isInMiddleRange)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Item is already in the middle range of the viewport, elapsed: {(DateTime.Now - startTime).TotalMilliseconds}ms");
                return;
            }
            
            // Calculate the new scroll position to center the item in the viewport
            // This is a direct calculation based on the current position of the item
            double targetOffset = scrollViewer.Offset.Y + itemTop - ((viewportHeight - itemHeight) / 2);
            
            // Ensure the offset is within valid bounds
            targetOffset = Math.Max(0, Math.Min(targetOffset, scrollViewer.Extent.Height - viewportHeight));
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Centering item: itemTop={itemTop}, viewportHeight={viewportHeight}, itemHeight={itemHeight}, currentOffset={scrollViewer.Offset.Y}, targetOffset={targetOffset}, elapsed: {(DateTime.Now - startTime).TotalMilliseconds}ms");
            
            // Set the new scroll position directly
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, targetOffset);
            
            // Force layout update to ensure the scroll position is applied
            scrollViewer.InvalidateArrange();
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Scroll operation completed, elapsed: {(DateTime.Now - startTime).TotalMilliseconds}ms");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Error centering item: {ex.Message}, elapsed: {(DateTime.Now - startTime).TotalMilliseconds}ms");
        }
    }
            
    // Helper method to check if an item is within the middle range of the viewport
    private static bool IsItemInMiddleRange(double itemTop, double itemBottom, double viewportHeight)
    {
        double middleRangeStart = viewportHeight * 0.25; // 25% from the top
        double middleRangeEnd = viewportHeight * 0.75;   // 75% from the top
        
        // Check if the item is fully within the middle range
        return (itemTop >= middleRangeStart && itemBottom <= middleRangeEnd) || 
               (itemTop <= middleRangeStart && itemBottom >= middleRangeEnd);
    }
}
