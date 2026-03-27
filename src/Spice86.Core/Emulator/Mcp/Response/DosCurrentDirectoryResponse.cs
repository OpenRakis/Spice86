namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record DosCurrentDirectoryResponse {
    public required string Drive { get; init; }

    public required string CurrentDirectory { get; init; }
}