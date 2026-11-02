namespace Spice86.Views;

using Avalonia.Controls;

using Spice86.ViewModels;

using System;
using System.Collections.Specialized;
using System.ComponentModel;

/// <summary>Progress window shown while the recorded-data dump writes to disk.</summary>
public sealed partial class DumpProgressWindow : Window {
    /// <summary>Initializes a new instance of <see cref="DumpProgressWindow"/>.</summary>
    public DumpProgressWindow() {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e) {
        if (DataContext is DumpProgressViewModel viewModel) {
            viewModel.Messages.CollectionChanged += OnMessagesChanged;
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        if (e.Action == NotifyCollectionChangedAction.Add && MessagesList.ItemCount > 0) {
            MessagesList.ScrollIntoView(MessagesList.ItemCount - 1);
        }
    }

    /// <summary>The window cannot be closed while the dump is still running.</summary>
    protected override void OnClosing(WindowClosingEventArgs e) {
        if (DataContext is DumpProgressViewModel { IsFinished: false }) {
            e.Cancel = true;
        }
        base.OnClosing(e);
    }
}
