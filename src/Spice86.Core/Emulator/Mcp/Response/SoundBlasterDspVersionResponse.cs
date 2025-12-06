namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record SoundBlasterDspVersionResponse : McpToolResponse {
    public required int MajorVersion { get; init; }

    public required int MinorVersion { get; init; }
}