namespace Spice86.Behaviors;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Spice86.ViewModels;

using System.Timers;

/// <summary>
/// Attached behavior for handling scrolling in the disassembly view.
/// This behavior encapsulates the UI-specific scrolling logic to improve separation of concerns.
/// </summary>
public class DisassemblyScrollBehavior {
    private const int AnimationFramesPerSecond = 60;

    // Attached property for enabling the behavior
    public static readonly AttachedProperty<bool> IsEnabledProperty = AvaloniaProperty.RegisterAttached<DisassemblyScrollBehavior, Control, bool>("IsEnabled");

    // Attached property for the target address
    public static readonly AttachedProperty<uint> TargetAddressProperty = AvaloniaProperty.RegisterAttached<DisassemblyScrollBehavior, Control, uint>("TargetAddress");

    // Static field to track if we're currently processing a scroll operation
    private static bool _isScrollingInProgress;

    // Configuration properties for smooth scrolling
    private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(250);

    // Static constructor to register property changed handlers
    static DisassemblyScrollBehavior() {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
        TargetAddressProperty.Changed.AddClassHandler<Control>(OnTargetAddressChanged);
    }

    // Helper methods for getting/setting the attached properties
    public static bool GetIsEnabled(Control control) {
        return control.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(Control control, bool value) {
        control.SetValue(IsEnabledProperty, value);
    }

    public static uint GetTargetAddress(Control control) {
        return control.GetValue(TargetAddressProperty);
    }

    public static void SetTargetAddress(Control control, uint value) {
        control.SetValue(TargetAddressProperty, value);
    }

    // Handler for when IsEnabled property changes
    private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs e) {
        if (control is not ListBox listBox) {
            return;
        }

        bool isEnabled = (bool)e.NewValue!;
        Console.WriteLine($"IsEnabled changed to {isEnabled}");

        if (isEnabled) {
            // Initialize any resources needed
            Console.WriteLine("DisassemblyScrollBehavior enabled");

            // Subscribe to the Loaded event to handle initial centering
            listBox.Loaded += OnListBoxLoaded;

            // If the ListBox is already loaded, check if we need to scroll
            if (listBox.IsLoaded) {
                uint targetAddress = GetTargetAddress(listBox);
                if (targetAddress != 0) {
                    // Delay the scroll to ensure the UI has fully loaded
                    Dispatcher.UIThread.Post(() => ScrollToAddress(listBox, targetAddress), DispatcherPriority.Loaded);
                }
            }
        } else {
            // Clean up any resources
            Console.WriteLine("DisassemblyScrollBehavior disabled");
            listBox.Loaded -= OnListBoxLoaded;
        }
    }

    // Handler for ListBox Loaded event
    private static void OnListBoxLoaded(object? sender, EventArgs e) {
        if (sender is not ListBox listBox) {
            return;
        }

        Console.WriteLine("OnListBoxLoaded");

        // Unsubscribe from the Loaded event to avoid multiple calls
        listBox.Loaded -= OnListBoxLoaded;

        // Check if we have a target address for this ListBox
        uint targetAddress = GetTargetAddress(listBox);
        if (targetAddress != 0) {
            // Now that the ListBox is loaded, we can scroll to the target address
            Console.WriteLine($"ListBox now loaded, scrolling to target {targetAddress:X8}");
            // Delay the scroll to ensure the UI has fully loaded
            Dispatcher.UIThread.Post(() => ScrollToAddress(listBox, targetAddress), DispatcherPriority.Loaded);
        }
    }

    // Handler for when TargetAddress property changes
    private static void OnTargetAddressChanged(Control control, AvaloniaPropertyChangedEventArgs e) {
        if (control is not ListBox listBox || !GetIsEnabled(listBox)) {
            return;
        }

        uint targetAddress = (uint)e.NewValue!;
        Console.WriteLine($"OnTargetAddress changed to {targetAddress:X8}");

        // Only scroll if the ListBox is loaded
        if (listBox.IsLoaded) {
            // Directly scroll to the address
            ScrollToAddress(listBox, targetAddress);
        } else {
            Console.WriteLine($"ListBox IsLoaded: {listBox.IsLoaded}, IsVisible: {listBox.IsVisible}");
            Console.WriteLine($"ListBox not loaded yet, will scroll to {targetAddress:X8} when loaded");

            // Subscribe to Loaded event if not already subscribed
            listBox.Loaded -= OnListBoxLoaded;
            listBox.Loaded += OnListBoxLoaded;
        }
    }

    // Method to scroll to a specific address in the disassembly view
    public static void ScrollToAddress(ListBox listBox, uint targetAddress) {
        Console.WriteLine($"ScrollToAddress called to go to address {targetAddress:X8}");
        // Ensure we're on the UI thread
        if (!Dispatcher.UIThread.CheckAccess()) {
            Dispatcher.UIThread.Post(() => ScrollToAddress(listBox, targetAddress));

            return;
        }

        // Prevent re-entrancy
        if (_isScrollingInProgress) {
            return;
        }

        _isScrollingInProgress = true;
        try {
            DateTime startTime = DateTime.Now;
            Console.WriteLine($"[{startTime:HH:mm:ss.fff}] Starting scroll to address {targetAddress:X8}");

            // Find the ScrollViewer
            ScrollViewer? scrollViewer = listBox.FindDescendantOfType<ScrollViewer>() ?? listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (scrollViewer == null) {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ScrollViewer not found, elapsed: {(DateTime.Now - startTime).TotalMilliseconds}ms");

                return;
            }

            // Get the view model and find the target item
            if (listBox.DataContext is IModernDisassemblyViewModel viewModel) {
                // Get the target item using the O(1) lookup
                DebuggerLineViewModel? targetItem = viewModel.GetLineByAddress(targetAddress);
                if (targetItem == null) {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Could not find instruction with address {targetAddress:X8}, elapsed: {(DateTime.Now - startTime).TotalMilliseconds}ms");

                    return;
                }

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Found target item with address {targetItem.Address:X8}, elapsed: {(DateTime.Now - startTime).TotalMilliseconds}ms");

                // Find the index of the target item in the sorted collection
                int targetIndex = viewModel.SortedDebuggerLinesView.IndexOf(targetItem);
                if (targetIndex == -1) {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Target item not found in sorted view, elapsed: {(DateTime.Now - startTime).TotalMilliseconds}ms");

                    return;
                }

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Target item index: {targetIndex}, elapsed: {(DateTime.Now - startTime).TotalMilliseconds}ms");

                // Scroll to the target item
                Dispatcher.UIThread.Post(() => ScrollToPosition(listBox, scrollViewer, startTime, targetIndex), DispatcherPriority.Loaded);

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Scroll operation completed, elapsed: {(DateTime.Now - startTime).TotalMilliseconds}ms");
            } else {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] DataContext is not IModernDisassemblyViewModel, elapsed: {(DateTime.Now - startTime).TotalMilliseconds}ms");
            }
        } catch (Exception ex) {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Error scrolling to address: {ex.Message}");
        } finally {
            // Release the lock
            _isScrollingInProgress = false;
        }
    }

    private static void ScrollToPosition(ListBox listBox, ScrollViewer scrollViewer, DateTime startTime, int targetIndex) {
        // Determine the line height using the listBox.ItemCount for deterministic calculation
        // Try to calculate line height from extent and item count
        int itemCount = listBox.ItemCount;
        if (itemCount <= 0) {
            Console.WriteLine($"ItemCount is {itemCount}, cannot calculate line height");

            return;
        }

        double extentHeight = scrollViewer.Extent.Height;
        if (extentHeight <= 0) {
            Console.WriteLine($"Extent height is {extentHeight}, cannot calculate line height");

            return;
        }

        // Calculate relevant positions
        double lineHeight = extentHeight / itemCount;
        double topMargin = 4 * lineHeight;
        double bottomMargin = 6 * lineHeight;
        double currentOffset = scrollViewer.Offset.Y;
        double targetAreaTop = currentOffset + topMargin;
        double viewportHeight = scrollViewer.Viewport.Height;
        double targetAreaBottom = currentOffset + viewportHeight - bottomMargin;
        double itemPosition = targetIndex * lineHeight;

        // If we're already in target area, we don't need to scroll.
        if (itemPosition >= targetAreaTop && itemPosition <= targetAreaBottom) {
            return;
        }

        double targetOffset;
        // If the target position is in the bottom margin, we scroll to the bottom margin. This prevents jarring scroll jumps.
        if (itemPosition > targetAreaBottom && itemPosition < currentOffset + viewportHeight) {
            targetOffset = itemPosition - viewportHeight + bottomMargin;
        } else {
            // By default, we jump to the top margin.
            targetOffset = itemPosition - topMargin;
        }

        // Ensure the offset is within valid bounds
        double maxOffset = Math.Max(0, scrollViewer.Extent.Height - viewportHeight);
        double finalOffset = Math.Min(targetOffset, maxOffset);

        // Use smooth scrolling with configurable parameters
        AnimateSmoothScroll(scrollViewer, finalOffset);
    }

    private static void AnimateSmoothScroll(ScrollViewer scrollViewer, double targetOffsetY) {
        // Get the current offset
        double startOffsetY = scrollViewer.Offset.Y;

        // If we're already at the target, no need to animate
        if (Math.Abs(startOffsetY - targetOffsetY) < 0.1) {
            return;
        }

        // Ensure we're on the UI thread
        if (!Dispatcher.UIThread.CheckAccess()) {
            Dispatcher.UIThread.Post(() => AnimateSmoothScroll(scrollViewer, targetOffsetY));

            return;
        }

        // Use a timer to animate the scroll
        var timer = new Timer(1000.0 / AnimationFramesPerSecond);
        int currentFrame = 0;
        int totalFrames = (int)(AnimationDuration.TotalMilliseconds / (1000.0 / AnimationFramesPerSecond));

        timer.Elapsed += (sender, e) => {
            // Calculate progress (0.0 to 1.0)
            currentFrame++;
            double progress = Math.Min(1.0, currentFrame / (double)totalFrames);

            // Apply easing function
            double easedProgress = progress < 0.5 ? 4 * progress * progress * progress : 1 - Math.Pow(-2 * progress + 2, 3) / 2;

            // Calculate new offset
            double newOffsetY = startOffsetY + (targetOffsetY - startOffsetY) * easedProgress;

            // Apply the new offset on the UI thread
            Dispatcher.UIThread.Post(() => {
                scrollViewer.Offset = new Vector(scrollViewer.Offset.X, newOffsetY);
            });

            // Stop the timer when animation is complete
            if (progress >= 1.0) {
                timer.Stop();
                timer.Dispose();
            }
        };

        // Start the timer
        timer.Start();
    }
}