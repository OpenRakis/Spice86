namespace Spice86.Views;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using Spice86.ViewModels;

using System;
using System.ComponentModel;

public partial class ModernDisassemblyView : UserControl {
    // Cache for the ordered keys to avoid recreating the list on every GetIndex call
    private ListBox? _listBox;
    private ModernDisassemblyViewModel? ViewModel => DataContext as ModernDisassemblyViewModel;

    // Timer for debouncing scroll operations
    private DispatcherTimer? _scrollDebounceTimer;

    public ModernDisassemblyView() {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e) {
        base.OnLoaded(e);
        // Get references to UI elements
        _listBox = this.FindControl<ListBox>("DisassemblyListBox");

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
        if (ViewModel == null || _listBox == null) {
            return;
        }

        // Check if the address exists in the collection
        if (ViewModel.DebuggerLines.TryGetValue(address, out DebuggerLineViewModel? line)) {
            // Force the line to update its highlighting state
            line.UpdateIsCurrentInstruction();
            
            // Use the ListBox's built-in scrolling
            _listBox.ScrollIntoView(line);
            
            Console.WriteLine($"Scrolled to address {address:X8}");
        } else {
            Console.WriteLine($"Address {address:X8} not found in the disassembly");
        }
    }

    private void ScrollToCurrentInstruction() {
        if (_listBox == null || ViewModel == null || ViewModel.DebuggerLines.Count == 0) {
            return;
        }

        // Find the current instruction
        uint address = ViewModel.CurrentlyFocusedAddress;
        if (ViewModel.DebuggerLines.TryGetValue(address, out DebuggerLineViewModel? line)) {
            // Force the line to update its highlighting state
            line.UpdateIsCurrentInstruction();
            
            // Use the ListBox's built-in scrolling
            _listBox.ScrollIntoView(line);
            
            Console.WriteLine($"Scrolled to instruction at address {address:X8}");
        } else {
            Console.WriteLine($"Couldn't find line with address {address:X8}");
        }
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        // Handle property changes from the view model
        if (e.PropertyName is nameof(ModernDisassemblyViewModel.DebuggerLines)) {
            // When the debugger lines are updated, reset the items repeater
            if (_listBox != null) {
                _listBox.ItemsSource = ViewModel?.DebuggerLines.Values;
            }
        }
        // Only scroll to the current instruction when the IP changes or when pausing
        else if (e.PropertyName is nameof(ModernDisassemblyViewModel.IpPhysicalAddress) ||
                 e.PropertyName is nameof(ModernDisassemblyViewModel.CurrentlyFocusedAddress) ||
                (e.PropertyName is nameof(ModernDisassemblyViewModel.IsPaused) &&
                 sender is ModernDisassemblyViewModel viewModel &&
                 viewModel.IsPaused)) {
            
            // Avoid multiple scroll operations by using a debounce mechanism
            // Cancel any pending scroll operation
            _scrollDebounceTimer?.Stop();
            
            // Create a new timer if needed
            if (_scrollDebounceTimer == null) {
                _scrollDebounceTimer = new DispatcherTimer {
                    Interval = TimeSpan.FromMilliseconds(50)
                };
                _scrollDebounceTimer.Tick += (_, _) => {
                    _scrollDebounceTimer?.Stop();
                    
                    // Force a refresh of the ListBox to update the highlighting
                    if (_listBox != null && ViewModel != null) {
                        // This will force the ListBox to re-evaluate all bindings
                        _listBox.InvalidateVisual();
                        
                        // Force a refresh of the items collection to update the IsCurrentInstruction property
                        {
                            // Store the current items source

                            // Reset the items source to force a refresh
                            _listBox.ItemsSource = null;
                            _listBox.ItemsSource = ViewModel.DebuggerLines.Values;
                            
                            // Update the previous and current instruction highlighting
                            foreach (var line in ViewModel.DebuggerLines.Values) {
                                line.UpdateIsCurrentInstruction();
                            }
                        }
                    }
                    
                    // Scroll to the current instruction after refreshing
                    ScrollToCurrentInstruction();
                };
            }
            
            // Start the timer to trigger the scroll operation after a short delay
            _scrollDebounceTimer.Start();
        }
    }

    private void Instruction_PointerPressed(object sender, PointerPressedEventArgs e) {
        if (ViewModel == null) {
            return;
        }

        // Get the instruction from the sender's DataContext
        if (sender is ContentControl {DataContext: DebuggerLineViewModel debuggerLine}) {
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