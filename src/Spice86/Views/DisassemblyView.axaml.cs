namespace Spice86.Views;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

using System.ComponentModel;

using Spice86.ViewModels;

/// <summary>
/// View for the disassembly interface.
/// </summary>
public partial class DisassemblyView : UserControl {
    private IDisassemblyViewModel? _viewModel;
    private bool _isAttachedToVisualTree;
    private ScrollViewer? _scrollViewer;

    /// <summary>
    /// Initializes a new instance of the <see cref="DisassemblyView"/> class.
    /// </summary>
    public DisassemblyView() {
        InitializeComponent();
        DataContextChanged += DisassemblyView_DataContextChanged;

        // Subscribe to attached/detached events
        AttachedToVisualTree += DisassemblyView_AttachedToVisualTree;
        DetachedFromVisualTree += DisassemblyView_DetachedFromVisualTree;
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }

    private void DisassemblyView_DataContextChanged(object? sender, EventArgs e) {
        // Unsubscribe from the old view model if it exists
        if (_viewModel != null) {
            if (_isAttachedToVisualTree) {
                _viewModel.Deactivate();
            }
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        // Subscribe to the new view model
        _viewModel = DataContext as IDisassemblyViewModel;

        if (_viewModel != null) {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            if (_isAttachedToVisualTree) {
                _viewModel.Activate();
            }
        }
    }

    private void DisassemblyView_AttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) {
        _isAttachedToVisualTree = true;
        // Activate the view model when the view is attached to the visual tree
        _viewModel?.Activate();
        SubscribeToScrollViewer();
    }

    private void DisassemblyView_DetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) {
        _isAttachedToVisualTree = false;
        // Deactivate the view model when the view is detached from the visual tree
        _viewModel?.Deactivate();
        UnsubscribeFromScrollViewer();
    }

    private void SubscribeToScrollViewer() {
        ListBox? listBox = this.FindControl<ListBox>("DisassemblyListBox");
        if (listBox == null) {
            return;
        }
        _scrollViewer = listBox.FindDescendantOfType<ScrollViewer>();
        if (_scrollViewer != null) {
            _scrollViewer.ScrollChanged += OnScrollChanged;
            ReportVisibleRange(_scrollViewer, listBox.ItemCount);
        }
    }

    private void UnsubscribeFromScrollViewer() {
        if (_scrollViewer != null) {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer = null;
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e) {
        if (sender is not ScrollViewer scrollViewer || _viewModel == null) {
            return;
        }
        ListBox? listBox = this.FindControl<ListBox>("DisassemblyListBox");
        if (listBox == null) {
            return;
        }
        ReportVisibleRange(scrollViewer, listBox.ItemCount);
    }

    private void ReportVisibleRange(ScrollViewer scrollViewer, int itemCount) {
        if (_viewModel == null || itemCount <= 0) {
            return;
        }
        double extentHeight = scrollViewer.Extent.Height;
        if (extentHeight <= 0) {
            return;
        }
        double lineHeight = extentHeight / itemCount;
        if (lineHeight <= 0) {
            return;
        }
        int firstVisible = (int)(scrollViewer.Offset.Y / lineHeight);
        int lastVisible = (int)((scrollViewer.Offset.Y + scrollViewer.Viewport.Height) / lineHeight);
        _viewModel.OnVisibleRangeChanged(firstVisible, lastVisible);
    }

    private static void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
    }

    private void OnBreakpointClicked(object? sender, TappedEventArgs e) {
        if (sender is not Control { DataContext: DebuggerLineViewModel debuggerLine }
            || _viewModel == null
            || !_viewModel.ToggleBreakpointCommand.CanExecute(debuggerLine)) {
            return;
        }
        _viewModel.ToggleBreakpointCommand.Execute(debuggerLine);
        e.Handled = true;
    }

    private void OnBranchTargetClicked(object? sender, TappedEventArgs e) {
        if (sender is Control control &&
            control.DataContext is DebuggerLineViewModel debuggerLine &&
            debuggerLine.BranchTarget != null &&
            _viewModel != null) {
            object branchTarget = debuggerLine.BranchTarget;
            if (_viewModel.GoToAddressCommand.CanExecute(branchTarget)) {
                _viewModel.GoToAddressCommand.Execute(branchTarget);
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// Event handler for when the function selection AutoCompleteBox gets focus.
    /// </summary>
    /// <param name="sender">The sender object.</param>
    /// <param name="e">The focus event arguments.</param>
    private void OnFunctionSelectionFocus(object? sender, GotFocusEventArgs e) {
        if (sender is AutoCompleteBox autoCompleteBox) {
            // Clear the text when the AutoCompleteBox gets focus
            autoCompleteBox.Text = string.Empty;
        }
    }

    private void OnFunctionSelectionKeyDown(object? sender, KeyEventArgs e) {
        if (e.Key == Key.Enter && _viewModel != null) {
            // Execute the "Go to Function" command when Enter is pressed
            if (_viewModel.GoToFunctionCommand.CanExecute(_viewModel.SelectedFunction)) {
                _viewModel.GoToFunctionCommand.Execute(_viewModel.SelectedFunction);
                e.Handled = true;
            }
        }
    }
}