namespace Spice86.Core.Emulator.Mcp.Response;

public sealed record EmsHandleInfo {
    public required int HandleId { get; init; }
    public required int AllocatedPages { get; init; }
    public required string Name { get; init; }
}
