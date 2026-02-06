namespace Spice86.Core.Emulator.Mcp.Request;

/// <summary>
/// Request DTO for read_io_port tool.
/// </summary>
public sealed record ReadIoPortRequest {
    /// <summary>
    /// Gets the IO port number (0-65535).
    /// </summary>
    public required int Port { get; init; }
}
