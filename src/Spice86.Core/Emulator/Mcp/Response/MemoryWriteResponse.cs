namespace Spice86.Core.Emulator.Mcp.Response;

using Spice86.Shared.Emulator.Memory;

internal sealed record MemoryWriteResponse {
    public required SegmentedAddress Address { get; init; }

    public required int Length { get; init; }

    public required bool Success { get; init; }
}
