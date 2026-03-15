namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Spice86.Core.Emulator.Mcp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

/// <summary>
/// ViewModel for displaying MCP server status in the UI.
/// </summary>
public sealed partial class McpStatusViewModel : ViewModelBase {
    private readonly Func<int> _getAvailableToolsCount;

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
        _getAvailableToolsCount = () => mcpServer.GetAvailableTools().Length;
        _port = port;
        _statusText = "MCP Server Active (Legacy)";
        _isServerRunning = true;

        foreach (Tool tool in mcpServer.GetAllTools()) {
            Tools.Add(new McpToolViewModel(tool.Name, tool.Description ?? string.Empty, true, true,
                enabled => mcpServer.SetToolEnabled(tool.Name, enabled), UpdateAvailableToolsCount));
        }

        UpdateAvailableToolsCount();
    }

    public McpStatusViewModel(IEnumerable<ModernMcpToolDescriptor> tools, int port = 8081) {
        _getAvailableToolsCount = () => Tools.Count(t => t.IsEnabled);
        _port = port;
        _statusText = "MCP Server Active (Modern)";
        _isServerRunning = true;

        foreach (ModernMcpToolDescriptor tool in tools) {
            Tools.Add(new McpToolViewModel(tool.Name, tool.Description, true, false));
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
        AvailableToolsCount = _getAvailableToolsCount();
    }

    public static IReadOnlyList<ModernMcpToolDescriptor> DiscoverModernTools(IEnumerable<Assembly> assemblies) {
        List<ModernMcpToolDescriptor> tools = new();
        HashSet<string> seenNames = new(StringComparer.Ordinal);

        foreach (Assembly assembly in assemblies) {
            foreach (Type type in assembly.GetTypes()) {
                if (type.GetCustomAttribute<McpServerToolTypeAttribute>() == null) {
                    continue;
                }

                MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                foreach (MethodInfo method in methods) {
                    McpServerToolAttribute? toolAttribute = method.GetCustomAttribute<McpServerToolAttribute>();
                    if (toolAttribute == null) {
                        continue;
                    }

                    string toolName = string.IsNullOrWhiteSpace(toolAttribute.Name)
                        ? method.Name
                        : toolAttribute.Name;
                    if (!seenNames.Add(toolName)) {
                        continue;
                    }

                    string description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
                    tools.Add(new ModernMcpToolDescriptor(toolName, description));
                }
            }
        }

        return tools;
    }
}

public sealed record ModernMcpToolDescriptor(string Name, string Description);
