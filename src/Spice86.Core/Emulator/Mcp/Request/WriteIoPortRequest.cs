namespace Spice86.Core.Emulator.Mcp.Request;

/// <summary>
/// Request DTO for write_io_port tool.
/// </summary>
public sealed record WriteIoPortRequest {
    /// <summary>
    /// Gets the IO port number (0-65535).
    /// </summary>
    public required int Port { get; init; }

    /// <summary>
    /// Gets the value to write (0-255).
    /// </summary>
    public required int Value { get; init; }
}
