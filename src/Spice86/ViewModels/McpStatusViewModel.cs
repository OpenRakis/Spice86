namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using Spice86.Core.Emulator.Mcp;
using ModelContextProtocol.Protocol;

/// <summary>
/// ViewModel for displaying MCP server status in the UI.
/// </summary>
public sealed partial class McpStatusViewModel : ViewModelBase {
    private readonly IMcpServer _mcpServer;

    [ObservableProperty]
    private bool _isServerRunning;

    [ObservableProperty]
    private string _statusText;

    [ObservableProperty]
    private int _availableToolsCount;

    [ObservableProperty]
    private int _port;

    public ObservableCollection<McpToolViewModel> Tools { get; } = new();

    public McpStatusViewModel(IMcpServer mcpServer, int port = 8081) {
        _mcpServer = mcpServer;
        _port = port;
        _statusText = "MCP Server Active";
        _isServerRunning = true;

        foreach (Tool tool in _mcpServer.GetAllTools()) {
            Tools.Add(new McpToolViewModel(_mcpServer, tool.Name, tool.Description ?? string.Empty, true, UpdateAvailableToolsCount));
        }
        
        UpdateAvailableToolsCount();
    }

    /// <summary>
    /// Updates the status information from the MCP server.
    /// </summary>
    public void UpdateStatus() {
        IsServerRunning = true;
        UpdateAvailableToolsCount();
        StatusText = $"MCP Server Active - {AvailableToolsCount} tools available";
    }

    private void UpdateAvailableToolsCount() {
        AvailableToolsCount = _mcpServer.GetAvailableTools().Length;
    }
}
