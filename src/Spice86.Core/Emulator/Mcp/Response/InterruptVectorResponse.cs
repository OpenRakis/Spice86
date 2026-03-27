namespace Spice86.Core.Emulator.Mcp.Response;

using Spice86.Shared.Emulator.Memory;

internal sealed record InterruptVectorResponse {
    public required int VectorNumber { get; init; }

    public required SegmentedAddress Address { get; init; }
}