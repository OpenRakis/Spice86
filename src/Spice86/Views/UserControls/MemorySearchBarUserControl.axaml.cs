namespace Spice86.Views.UserControls;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using Spice86.ViewModels;

using System.ComponentModel;

public partial class MemorySearchBarUserControl : UserControl {
    private INotifyPropertyChanged? _observableDataContext;

    public MemorySearchBarUserControl() {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        UnsubscribeFromDataContext();
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e) {
        UnsubscribeFromDataContext();

        _observableDataContext = DataContext as INotifyPropertyChanged;
        if (_observableDataContext is not null) {
            _observableDataContext.PropertyChanged += OnDataContextPropertyChanged;
        }
    }

    private void UnsubscribeFromDataContext() {
        if (_observableDataContext is null) {
            return;
        }

        _observableDataContext.PropertyChanged -= OnDataContextPropertyChanged;
        _observableDataContext = null;
    }

    private void OnDataContextPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName != nameof(IMemorySearchViewModel.IsSearchingMemory) || DataContext is not IMemorySearchViewModel viewModel || !viewModel.IsSearchingMemory) {
            return;
        }

        FocusSearchBox();
    }

    private void FocusSearchBox() {
        Dispatcher.UIThread.Post(() => {
            TextBox? searchTextBox = this.FindControl<TextBox>("SearchTextBox");
            if (searchTextBox is null) {
                return;
            }

            searchTextBox.Focus();
            searchTextBox.SelectAll();
        }, DispatcherPriority.Background);
    }

    private void OnSearchTextBoxKeyDown(object? sender, KeyEventArgs e) {
        if (DataContext is not IMemorySearchViewModel viewModel || viewModel.IsBusy) {
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers != KeyModifiers.Shift) {
            ExecuteSearch(viewModel);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.E && e.KeyModifiers == KeyModifiers.Control) {
            ExecuteSearch(viewModel);
            e.Handled = true;
        }
    }

    private static void ExecuteSearch(IMemorySearchViewModel viewModel) {
        if (viewModel.NextOccurrenceAction.CanExecute(null)) {
            viewModel.NextOccurrenceAction.Execute(null);
        }
    }
}