namespace Spice86.Core.Emulator.Mcp.Response;

/// <summary>
/// Response returned after an IO port read operation.
/// Contains the port number that was read and the value obtained from the port.
/// </summary>
public sealed record IoPortReadResponse : McpToolResponse {
    /// <summary>
    /// The IO port number that was read.
    /// </summary>
    public required int Port { get; init; }
    /// <summary>
    /// The value read from the IO port.
    /// </summary>
    public required byte Value { get; init; }
}
