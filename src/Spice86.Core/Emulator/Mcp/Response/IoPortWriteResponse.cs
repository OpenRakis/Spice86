namespace Spice86.Core.Emulator.Mcp.Response;

/// <summary>
/// Response returned after an IO port write operation.
/// Contains the port number, the value written, and whether the operation was successful.
/// </summary>
public sealed record IoPortWriteResponse : McpToolResponse {
    /// <summary>
    /// The IO port number to which the value was written.
    /// </summary>
    public required int Port { get; init; }
    /// <summary>
    /// The value that was written to the IO port.
    /// </summary>
    public required int Value { get; init; }
    /// <summary>
    /// Indicates whether the write operation was successful.
    /// </summary>
    public required bool Success { get; init; }
}
