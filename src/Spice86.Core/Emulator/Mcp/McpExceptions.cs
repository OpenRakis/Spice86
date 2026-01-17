namespace Spice86.Core.Emulator.Mcp;

using System;

/// <summary>
/// Base exception for MCP server errors.
/// </summary>
public class McpException : Exception {
    public int ErrorCode { get; }

    public McpException(string message, int errorCode) : base(message) {
        ErrorCode = errorCode;
    }

    public McpException(string message, int errorCode, Exception innerException) 
        : base(message, innerException) {
        ErrorCode = errorCode;
    }
}

/// <summary>
/// Exception for invalid method errors (-32601).
/// </summary>
public sealed class McpMethodNotFoundException : McpException {
    public McpMethodNotFoundException(string methodName) 
        : base($"Method not found: {methodName}", -32601) {
    }
}

/// <summary>
/// Exception for invalid parameters (-32602).
/// </summary>
public sealed class McpInvalidParametersException : McpException {
    public McpInvalidParametersException(string message) 
        : base(message, -32602) {
    }
}

/// <summary>
/// Exception for internal errors (-32603).
/// </summary>
public sealed class McpInternalErrorException : McpException {
    public McpInternalErrorException(string message) 
        : base(message, -32603) {
    }

    public McpInternalErrorException(string message, Exception innerException) 
        : base(message, -32603, innerException) {
    }
}
