namespace Spice86.Views.Behaviors;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Spice86.Shared.Emulator.Memory;
using Spice86.ViewModels;



/// <summary>
/// Attached behavior for handling scrolling in the disassembly view.
/// This behavior encapsulates the UI-specific scrolling logic to improve separation of concerns.
/// </summary>
public class DisassemblyScrollBehavior {
    // Attached property for enabling the behavior
    public static readonly AttachedProperty<bool> IsEnabledProperty = AvaloniaProperty.RegisterAttached<DisassemblyScrollBehavior, Control, bool>("IsEnabled");

    // Attached property for the target address
    public static readonly AttachedProperty<SegmentedAddress> TargetAddressProperty = AvaloniaProperty.RegisterAttached<DisassemblyScrollBehavior, Control, SegmentedAddress>("TargetAddress");

    // Static field to track if we're currently processing a scroll operation
    private static bool _isScrollingInProgress;

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

    public static SegmentedAddress GetTargetAddress(Control control) {
        return control.GetValue(TargetAddressProperty);
    }

    public static void SetTargetAddress(Control control, SegmentedAddress value) {
        control.SetValue(TargetAddressProperty, value);
    }

    // Handler for when IsEnabled property changes
    private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs e) {
        if (control is not ListBox listBox) {
            return;
        }

        bool isEnabled = (bool)e.NewValue!;
        if (isEnabled) {
            // Subscribe to the Loaded event to handle initial centering
            listBox.Loaded += OnListBoxLoaded;

            // If the ListBox is already loaded, scroll immediately (no deferred dispatch)
            if (listBox.IsLoaded) {
                uint targetAddress = GetTargetAddress(listBox).Linear;
                if (targetAddress != 0) {
                    ScrollToAddress(listBox, targetAddress);
                }
            }
        } else {
            listBox.Loaded -= OnListBoxLoaded;
        }
    }

    // Handler for ListBox Loaded event
    private static void OnListBoxLoaded(object? sender, EventArgs e) {
        if (sender is not ListBox listBox) {
            return;
        }

        // Unsubscribe from the Loaded event to avoid multiple calls
        listBox.Loaded -= OnListBoxLoaded;

        // Check if we have a target address for this ListBox
        uint targetAddress = GetTargetAddress(listBox).Linear;
        if (targetAddress != 0) {
            ScrollToAddress(listBox, targetAddress);
        }
    }

    // Handler for when TargetAddress property changes
    private static void OnTargetAddressChanged(Control control, AvaloniaPropertyChangedEventArgs e) {
        if (control is not ListBox listBox || !GetIsEnabled(listBox)) {
            return;
        }
        uint targetAddress = ((SegmentedAddress)e.NewValue!).Linear;
        if (listBox.IsLoaded) {
            ScrollToAddress(listBox, targetAddress);
        } else {
            listBox.Loaded -= OnListBoxLoaded;
            listBox.Loaded += OnListBoxLoaded;
        }
    }

    // Method to scroll to a specific address in the disassembly view
    public static void ScrollToAddress(ListBox listBox, uint targetAddress) {
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
            // Find the ScrollViewer
            ScrollViewer? scrollViewer = listBox.FindDescendantOfType<ScrollViewer>() ?? listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (scrollViewer == null) {
                return;
            }

            // Get the view model and find the target item
            if (listBox.DataContext is IDisassemblyViewModel viewModel) {
                if (!viewModel.TryGetLineByAddress(targetAddress, out DebuggerLineViewModel? targetItem)) {
                    return;
                }

                // Find the index of the target item in the sorted collection
                int targetIndex = viewModel.SortedDebuggerLinesView.IndexOf(targetItem);
                if (targetIndex == -1) {
                    return;
                }

                // Scroll to the target item directly, without an extra Dispatcher.Post
                // round-trip. The previous indirection caused the view to lag one or two
                // render frames behind the pause, producing a visible "auto-scroll" delay
                // when the emulator hit a breakpoint.
                ScrollToPosition(listBox, scrollViewer, targetIndex);
            }
        } finally {
            // Release the lock
            _isScrollingInProgress = false;
        }
    }

    private static void ScrollToPosition(ListBox listBox, ScrollViewer scrollViewer, int targetIndex) {
        int itemCount = listBox.ItemCount;
        if (itemCount <= 0) {
            return;
        }

        double extentHeight = scrollViewer.Extent.Height;
        if (extentHeight <= 0) {
            return;
        }

        double lineHeight = extentHeight / itemCount;
        // Calculate relevant positions
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

        // Instant scroll: set the offset directly.
        // Smooth animation was removed because each render frame of SelectableTextBlock rows with
        // inline Runs is too expensive on Linux (Skia/FreeType), turning a 250ms animation into 20s.
        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, finalOffset);
    }
}