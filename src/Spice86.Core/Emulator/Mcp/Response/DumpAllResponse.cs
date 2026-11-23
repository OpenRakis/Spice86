namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record DumpAllResponse {
    public required string Folder { get; init; }
}
