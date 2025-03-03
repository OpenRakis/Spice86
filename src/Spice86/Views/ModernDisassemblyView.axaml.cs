namespace Spice86.Views;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using Spice86.ViewModels;

using System.ComponentModel;
using System.Windows.Input;

public partial class ModernDisassemblyView : UserControl {
    // Cache for the ordered keys to avoid recreating the list on every GetIndex call
    private List<uint>? _cachedOrderedKeys;
    private ItemsRepeater? _itemsRepeater;
    private AvaloniaDictionary<uint, DebuggerLineViewModel>? _lastDictionaryReference;
    private ScrollViewer? _scrollViewer;
    private ModernDisassemblyViewModel? ViewModel => DataContext as ModernDisassemblyViewModel;

    public ModernDisassemblyView() {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e) {
        base.OnLoaded(e);
        // Get references to UI elements
        _scrollViewer = this.FindControl<ScrollViewer>("DisassemblyScrollViewer");
        _itemsRepeater = _scrollViewer?.FindControl<ItemsRepeater>("DisassemblyItemsRepeater");

        // Set up scroll to instruction handler
        if (ViewModel != null) {
            ViewModel.ScrollToAddress += ScrollToAddress;
            ViewModel.EnableEventHandlers();
        }

        // Initial scroll to current instruction after a short delay
        Dispatcher.UIThread.Post(ScrollToCurrentInstruction, DispatcherPriority.Background);
    }

    protected override void OnUnloaded(RoutedEventArgs e) {
        // Set up scroll to instruction handler
        if (ViewModel != null) {
            ViewModel.ScrollToAddress -= ScrollToAddress;
            ViewModel.DisableEventHandlers();
        }
        base.OnUnloaded(e);
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        if (ViewModel != null) {
            ICommand? command = e.Key switch {
                Key.Up => ViewModel.LineUpCommand,
                Key.Down => ViewModel.LineDownCommand,
                Key.PageUp => ViewModel.PageUpCommand,
                Key.PageDown => ViewModel.PageDownCommand,
                _ => null
            };

            if (command != null && command.CanExecute(null)) {
                command.Execute(null);
                e.Handled = true;

                return;
            }
        }

        base.OnKeyDown(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e) {
        if (ViewModel is not {IsPaused: true}) {
        } else {
            if (e.Delta.Y > 0) {
                // Scroll up
                if (ViewModel.LineUpCommand.CanExecute(null)) {
                    ViewModel.LineUpCommand.Execute(null);
                }
            } else if (e.Delta.Y < 0) {
                // Scroll down
                if (ViewModel.LineDownCommand.CanExecute(null)) {
                    ViewModel.LineDownCommand.Execute(null);
                }
            }

            e.Handled = true;
        }

        base.OnPointerWheelChanged(e);
    }

    protected override void OnDataContextChanged(EventArgs e) {
        // Unsubscribe from old view model
        if (ViewModel != null) {
            ViewModel.ScrollToAddress -= ScrollToAddress;
            if (DataContext is INotifyPropertyChanged oldViewModel) {
                oldViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }
        }

        base.OnDataContextChanged(e);

        // Subscribe to new view model
        if (DataContext is ModernDisassemblyViewModel newViewModel) {
            newViewModel.ScrollToAddress += ScrollToAddress;
            if (DataContext is INotifyPropertyChanged notifyViewModel) {
                notifyViewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
        }
    }

    private void ScrollToAddress(uint address) {
        if (ViewModel == null || _itemsRepeater == null || _scrollViewer == null) {
            return;
        }

        // Find the index of the instruction
        int index = GetIndex(address);
        if (index < 0) {
            return;
        }

        // Get the container for this item
        Control? container = _itemsRepeater.TryGetElement(index);
        if (container != null) {
            // Scroll to this container, centering it in the view
            // Calculate the position to scroll to (center the item in the viewport)
            Rect containerBounds = container.Bounds;
            double viewportHeight = _scrollViewer.Viewport.Height;

            // Calculate the target scroll position (center the item)
            double targetScrollPosition = containerBounds.Y - viewportHeight / 2 + containerBounds.Height / 2;

            // Ensure we don't scroll beyond bounds
            targetScrollPosition = Math.Max(0, targetScrollPosition);

            // Set the scroll position
            _scrollViewer.Offset = new Vector(0, targetScrollPosition);
        }
    }

    private void ScrollToCurrentInstruction() {
        if (_itemsRepeater == null || _scrollViewer == null || ViewModel == null || ViewModel.DebuggerLines.Count == 0) {
            return;
        }

        // Find the current instruction
        int currentIndex = GetIndex(ViewModel.CurrentlyFocusedAddress);
        if (currentIndex < 0) {
            Console.WriteLine($"Couldn't find line with address {ViewModel.CurrentlyFocusedAddress:X8}");

            return;
        }

        // Use ConfigureAwait(false) to avoid deadlocks and fire-and-forget the task
        _ = TryScrollToCurrentInstruction(currentIndex, 0);
    }

    private int GetIndex(uint address) {
        if (ViewModel == null) {
            return -1;
        }

        AvaloniaDictionary<uint, DebuggerLineViewModel> collection = ViewModel.DebuggerLines;

        // First check if the target exists (O(1) operation)
        if (!collection.ContainsKey(address)) {
            return -1;
        }

        // Use cached list if available and still valid, otherwise create a new one
        if (_cachedOrderedKeys == null || _lastDictionaryReference != collection) {
            _cachedOrderedKeys = [..collection.Keys];
            _lastDictionaryReference = collection;
        }

        // Use the cached list for lookup
        return _cachedOrderedKeys.IndexOf(address);
    }

    private async Task TryScrollToCurrentInstruction(int index, int attemptCount) {
        if (_itemsRepeater == null || _scrollViewer == null || attemptCount > 5) {
            return;
        }

        // Try to get the container
        Control? container = _itemsRepeater.TryGetElement(index);

        if (container != null) {
            // We found the container, scroll to it
            try {
                // Calculate the position to center the item in the viewport
                Rect containerBounds = container.Bounds;
                double viewportHeight = _scrollViewer.Viewport.Height;

                // Calculate the target offset to center the item
                double targetOffset = containerBounds.Y - viewportHeight / 2 + containerBounds.Height / 2;
                targetOffset = Math.Max(0, targetOffset); // Ensure we don't scroll past the top

                // Scroll to the calculated position
                _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, targetOffset);

                Console.WriteLine($"Successfully scrolled to instruction at index {index}, offset {targetOffset}");
            } catch (Exception ex) {
                Console.WriteLine($"Error scrolling to instruction: {ex.Message}");
            }
        } else {
            Console.WriteLine($"Container for instruction at index {index} not found, attempt {attemptCount + 1}");

            // If we can't find the container, try to scroll to approximately where it should be
            if (ViewModel?.DebuggerLines is {Count: > 0}) {
                // First attempt: use ItemsRepeater's built-in scrolling
                if (attemptCount == 0) {
                    try {
                        // This will attempt to bring the item into view and realize it
                        await ScrollToIndex(index);
                        Console.WriteLine($"Used ItemsRepeater ScrollIntoView for index {index}");
                    } catch (Exception ex) {
                        Console.WriteLine($"Error using ScrollIntoView: {ex.Message}");
                    }
                }
                // Second attempt: try approximate position
                else if (attemptCount == 1) {
                    // Approximate position based on index
                    double approximatePosition = (double)index / ViewModel.DebuggerLines.Count;
                    double estimatedOffset = approximatePosition * _scrollViewer.Extent.Height;

                    // Set an approximate scroll position to force container creation
                    _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, estimatedOffset);
                    Console.WriteLine($"Set approximate scroll position to {estimatedOffset}");
                }
                // Additional attempts: try scrolling a bit before and after the estimated position
                else {
                    double approximatePosition = (double)index / ViewModel.DebuggerLines.Count;
                    double estimatedOffset = approximatePosition * _scrollViewer.Extent.Height;

                    // Add some jitter based on attempt count to try different positions
                    double jitter = attemptCount % 2 == 0 ? 100 : -100;
                    _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, estimatedOffset + jitter);
                    Console.WriteLine($"Set jittered scroll position to {estimatedOffset + jitter}");
                }
            }

            // Schedule another attempt with a delay
            await Task.Delay(150 * (attemptCount + 1)); // Increasing delay for each attempt

            // Make sure we're on the UI thread when we try again
            await Dispatcher.UIThread.InvokeAsync(() => TryScrollToCurrentInstruction(index, attemptCount + 1));
        }
    }

    // Helper method to scroll to an index using ItemsRepeater's functionality
    private async Task ScrollToIndex(int index) {
        if (_itemsRepeater == null || _scrollViewer == null) {
            return;
        }

        // Force layout update to ensure items are measured
        _itemsRepeater.UpdateLayout();

        // Wait for layout to complete
        await Task.Delay(50);

        // Try to get the container after layout update
        Control? container = _itemsRepeater.TryGetElement(index);

        if (container != null) {
            // Scroll the container into view
            container.BringIntoView();

            // Wait for scrolling to complete
            await Task.Delay(50);
        } else {
            // If container still not realized, try to scroll approximately
            if (ViewModel?.DebuggerLines != null && ViewModel.DebuggerLines.Count > 0) {
                double approximatePosition = (double)index / ViewModel.DebuggerLines.Count;
                double estimatedOffset = approximatePosition * _scrollViewer.Extent.Height;

                // Set scroll position
                _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, estimatedOffset);
            }
        }
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        // Properties that should trigger scrolling to the current instruction
        if (e.PropertyName is nameof(ModernDisassemblyViewModel.CurrentlyFocusedAddress)) {
            // When instructions are updated or execution pauses, scroll to the current instruction
            Dispatcher.UIThread.Post(ScrollToCurrentInstruction, DispatcherPriority.Background);
        }
    }

    private void Instruction_PointerPressed(object sender, PointerPressedEventArgs e) {
        if (ViewModel == null) {
            return;
        }

        // Get the instruction from the sender's DataContext
        if (sender is Border {DataContext: DebuggerLineViewModel debuggerLine}) {
            // Set the selected instruction in the view model
            ViewModel.SelectedDebuggerLine = debuggerLine;

            // Handle double-click for creating breakpoint
            if (e.ClickCount == 2) {
                ViewModel.CreateExecutionBreakpointHereCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}