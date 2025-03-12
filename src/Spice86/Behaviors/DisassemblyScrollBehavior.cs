namespace Spice86.Behaviors;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

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
        }
    }

    // Handler for ListBox Loaded event
    private static void OnListBoxLoaded(object? sender, EventArgs e)
    {
        if (sender is ListBox listBox)
        {
            uint targetAddress = GetTargetAddress(listBox);
            if (targetAddress != 0)
            {
                // Delay the scroll to ensure the UI has fully loaded
                Dispatcher.UIThread.Post(() => ScrollToAddress(listBox, targetAddress), DispatcherPriority.Loaded);
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
        
        // Only scroll if the ListBox is loaded and visible
        if (listBox is {IsLoaded: true, IsVisible: true})
        {
            ScrollToAddress(listBox, targetAddress);
        }
        else
        {
            // If the ListBox is not loaded yet, we'll handle it in the Loaded event
            Console.WriteLine($"ListBox not loaded yet, will scroll to {targetAddress:X8} when loaded");
        }
    }

    // Main scrolling logic
    public static void ScrollToAddress(ListBox listBox, uint targetAddress)
    {
        // Ensure we're on the UI thread
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ScrollToAddress(listBox, targetAddress));
            return;
        }

        // Find the ScrollViewer - first try to get it directly from the template
        ScrollViewer? scrollViewer = listBox.FindDescendantOfType<ScrollViewer>();
        
        // If not found, try to get it from the visual tree
        if (scrollViewer == null)
        {
            scrollViewer = listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        }
        
        if (scrollViewer == null)
        {
            Console.WriteLine("ScrollViewer not found");
            return;
        }

        // Use the view model's GetLineByAddress method for efficient lookup
        if (listBox.DataContext is IModernDisassemblyViewModel viewModel)
        {
            DebuggerLineViewModel? targetItem = viewModel.GetLineByAddress(targetAddress);
            if (targetItem == null)
            {
                Console.WriteLine($"Could not find instruction with address {targetAddress:X8} in the dictionary");
                return;
            }

            Console.WriteLine($"Found target item with address {targetItem.Address:X8}");
            
            // Use a two-step approach for reliable scrolling
            // Step 1: First scroll the item into view to ensure the container is created
            listBox.ScrollIntoView(targetItem);
            
            // Step 2: After the container is created, calculate and apply the exact position
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    // Try to find the container for the target item
                    ListBoxItem? container = listBox.ContainerFromItem(targetItem) as ListBoxItem;
                    
                    if (container == null)
                    {
                        Console.WriteLine("Container not found after ScrollIntoView, trying visual tree search");
                        container = listBox.GetVisualDescendants()
                            .OfType<ListBoxItem>()
                            .FirstOrDefault(item => item.DataContext is DebuggerLineViewModel line && line.Address == targetAddress);
                    }
                    
                    if (container == null)
                    {
                        Console.WriteLine("Container still not found, cannot center item");
                        return;
                    }
                    
                    // Get the position and size of the container
                    double itemHeight = container.Bounds.Height;
                    double viewportHeight = scrollViewer.Viewport.Height;
                    double itemTop = container.Bounds.Y;
                    double itemBottom = itemTop + itemHeight;
                    
                    // Check if the item is already in the middle range of the viewport
                    if (IsWithinMiddleRangeOfViewPort(scrollViewer, itemTop, itemBottom))
                    {
                        Console.WriteLine("Item is already in the middle range of the viewport");
                        return;
                    }
                    
                    // Calculate the new scroll position to center the item
                    double targetOffset = itemTop - (viewportHeight / 2) + (itemHeight / 2);
                    
                    // Ensure the offset is within valid bounds
                    targetOffset = Math.Max(0, Math.Min(targetOffset, scrollViewer.Extent.Height - viewportHeight));
                    
                    Console.WriteLine($"Centering item: itemTop={itemTop}, viewportHeight={viewportHeight}, itemHeight={itemHeight}, targetOffset={targetOffset}");
                    
                    // Set the new scroll position
                    scrollViewer.Offset = new Vector(scrollViewer.Offset.X, targetOffset);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error centering item: {ex.Message}");
                }
            }, DispatcherPriority.Render);
        }
        else
        {
            Console.WriteLine("DataContext is not IModernDisassemblyViewModel");
        }
    }

    // Helper method to check if an item is within the middle range of the viewport
    private static bool IsWithinMiddleRangeOfViewPort(ScrollViewer scrollViewer, double itemTop, double itemBottom)
    {
        double viewportHeight = scrollViewer.Viewport.Height;
        double scrollOffset = scrollViewer.Offset.Y;
        double edgeZoneHeight = (itemBottom - itemTop) * 3;

        // Calculate the visible area boundaries
        double viewportTop = scrollOffset;
        double viewportBottom = viewportTop + viewportHeight;

        // Define the middle range of the viewport as 3 items from the top and bottom
        double middleRangeTop = viewportTop + edgeZoneHeight;
        double middleRangeBottom = viewportBottom - edgeZoneHeight;
        
        // Check if the item is in the middle range
        return (itemTop >= middleRangeTop && itemTop <= middleRangeBottom) ||
               (itemBottom >= middleRangeTop && itemBottom <= middleRangeBottom) ||
               (itemTop <= middleRangeTop && itemBottom >= middleRangeBottom);
    }

}
