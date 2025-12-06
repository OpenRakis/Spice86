namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record XmsHandleInfo {
    public required int HandleId { get; init; }

    public required int SizeKB { get; init; }

    public required bool IsLocked { get; init; }
}
