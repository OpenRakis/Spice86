namespace Spice86.Core.Emulator.Mcp.Response;

public sealed record EmsStateResponse : McpToolResponse {
    public required bool IsEnabled { get; init; }
    public required int PageFrameSegment { get; init; }
    public required int TotalPages { get; init; }
    public required int AllocatedPages { get; init; }
    public required int FreePages { get; init; }
    public required int PageSize { get; init; }
    public required EmsHandleInfo[] Handles { get; init; }
}
