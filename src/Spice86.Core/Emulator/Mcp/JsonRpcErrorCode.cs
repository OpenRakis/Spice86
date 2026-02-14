namespace Spice86.Core.Emulator.Mcp;

/// <summary>
/// JSON-RPC 2.0 error codes for MCP protocol.
/// </summary>
public enum JsonRpcErrorCode {
    /// <summary>
    /// Parse error - Invalid JSON was received by the server.
    /// </summary>
    ParseError = -32700,

    /// <summary>
    /// Invalid Request - The JSON sent is not a valid Request object.
    /// </summary>
    InvalidRequest = -32600,

    /// <summary>
    /// Method not found - The method does not exist / is not available.
    /// </summary>
    MethodNotFound = -32601,

    /// <summary>
    /// Invalid params - Invalid method parameter(s).
    /// </summary>
    InvalidParams = -32602,

    /// <summary>
    /// Internal error - Internal JSON-RPC error.
    /// </summary>
    InternalError = -32603
}
