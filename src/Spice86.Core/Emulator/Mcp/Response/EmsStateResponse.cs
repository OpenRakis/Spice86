namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record EmsStateResponse {
    public required bool IsEnabled { get; init; }

    public required int PageFrameSegment { get; init; }

    public required int TotalPages { get; init; }

    public required int AllocatedPages { get; init; }

    public required int FreePages { get; init; }

    public required int PageSize { get; init; }

    public required EmsHandleInfo[] Handles { get; init; }

    public required EmsPageMappingInfo[] PageMappings { get; init; }
}
