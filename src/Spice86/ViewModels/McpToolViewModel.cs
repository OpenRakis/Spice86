namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using Spice86.Core.Emulator.Mcp;
using System;

/// <summary>
/// ViewModel for a single MCP tool, allowing it to be enabled or disabled.
/// </summary>
public partial class McpToolViewModel : ViewModelBase {
    private readonly IMcpServer _mcpServer;
    private readonly Action? _onStatusChanged;

    [ObservableProperty]
    private string _name;
    
    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    private bool _isEnabled;

    public McpToolViewModel(IMcpServer mcpServer, string name, string description, bool isEnabled, Action? onStatusChanged = null) {
        _mcpServer = mcpServer;
        _name = name;
        _description = description;
        _isEnabled = isEnabled;
        _onStatusChanged = onStatusChanged;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e) {
        base.OnPropertyChanged(e);
        // Using nameof(IsEnabled) works because the source generator creates the property
        if (e.PropertyName == nameof(IsEnabled)) {
            _mcpServer.SetToolEnabled(Name, IsEnabled);
            _onStatusChanged?.Invoke();
        }
    }
}
