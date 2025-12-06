namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record EmsPageMappingInfo {
    public required int PhysicalPage { get; init; }

    public required int Segment { get; init; }

    public required bool IsMapped { get; init; }

    public int? HandleId { get; init; }

    public int? LogicalPage { get; init; }
}
