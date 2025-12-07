namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.Mcp;

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

    public McpStatusViewModel(IMcpServer mcpServer) {
        _mcpServer = mcpServer;
        _statusText = "MCP Server Active";
        _isServerRunning = true;
        _availableToolsCount = mcpServer.GetAvailableTools().Length;
    }

    /// <summary>
    /// Updates the status information from the MCP server.
    /// </summary>
    public void UpdateStatus() {
        IsServerRunning = true;
        AvailableToolsCount = _mcpServer.GetAvailableTools().Length;
        StatusText = $"MCP Server Active - {AvailableToolsCount} tools available";
    }
}
