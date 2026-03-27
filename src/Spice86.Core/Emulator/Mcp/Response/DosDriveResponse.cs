namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record DosDriveResponse {
    public required string Drive { get; init; }

    public required string CurrentDosDirectory { get; init; }

    public required string MountedHostDirectory { get; init; }
}