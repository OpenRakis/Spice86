namespace Spice86.Core.Emulator.Mcp.Response;

using Spice86.Shared.Emulator.Memory;

internal sealed record MemoryReadResponse : HexDataResponse {
    public required SegmentedAddress Address { get; init; }
}
