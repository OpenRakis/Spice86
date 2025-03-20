namespace Spice86.Views;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;
using System.ComponentModel;
using Spice86.ViewModels;

/// <summary>
/// View for displaying CPU registers.
/// </summary>
public partial class RegistersView : UserControl {
    private IRegistersViewModel? _viewModel;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="RegistersView"/> class.
    /// </summary>
    public RegistersView() {
        InitializeComponent();
        DataContextChanged += RegistersView_DataContextChanged;
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }

    private void RegistersView_DataContextChanged(object? sender, EventArgs e) {
        // Unsubscribe from the old view model if it exists
        if (_viewModel is INotifyPropertyChanged oldViewModel) {
            oldViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        // Subscribe to the new view model
        _viewModel = DataContext as IRegistersViewModel;
        if (_viewModel is INotifyPropertyChanged newViewModel) {
            newViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        // Handle property changes if needed
    }
}
