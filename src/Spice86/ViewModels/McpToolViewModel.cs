namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using System;

/// <summary>
/// ViewModel for a single MCP tool, allowing it to be enabled or disabled.
/// </summary>
public partial class McpToolViewModel : ViewModelBase {
    private readonly Action<bool>? _setEnabled;
    private readonly Action? _onStatusChanged;

    [ObservableProperty]
    private string _name;
    
    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _canToggle;

    public McpToolViewModel(string name, string description, bool isEnabled, bool canToggle,
        Action<bool>? setEnabled = null, Action? onStatusChanged = null) {
        _name = name;
        _description = description;
        _isEnabled = isEnabled;
        _canToggle = canToggle;
        _setEnabled = setEnabled;
        _onStatusChanged = onStatusChanged;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e) {
        base.OnPropertyChanged(e);
        // Using nameof(IsEnabled) works because the source generator creates the property
        if (e.PropertyName == nameof(IsEnabled) && _setEnabled != null) {
            _setEnabled(IsEnabled);
            _onStatusChanged?.Invoke();
        }
    }
}
