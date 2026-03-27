namespace Spice86.Core.Emulator.Mcp.Response;

using Spice86.Shared.Emulator.Memory;

internal sealed record MemorySearchResponse : PatternSearchResponse<SegmentedAddress> {

    public required SegmentedAddress StartAddress { get; init; }
}
