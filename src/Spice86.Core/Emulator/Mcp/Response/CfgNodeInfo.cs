namespace Spice86.Core.Emulator.Mcp.Response;

using Spice86.Shared.Emulator.Memory;

internal sealed record CfgNodeInfo {
    public required int Id { get; init; }

    public required SegmentedAddress Address { get; init; }

    public required int[] SuccessorIds { get; init; }

    public required int[] PredecessorIds { get; init; }

    public required bool IsLive { get; init; }
}
