namespace Spice86.Core.Emulator.Mcp;

using ModelContextProtocol.Protocol;

/// <summary>
/// MCP server for AI tools to inspect emulator state via JSON-RPC.
/// </summary>
public interface IMcpServer {
    string HandleRequest(string requestJson);
    Tool[] GetAvailableTools();
}