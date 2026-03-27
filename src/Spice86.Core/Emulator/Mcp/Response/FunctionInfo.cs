namespace Spice86.Core.Emulator.Mcp.Response;

using Spice86.Shared.Emulator.Memory;

internal sealed record FunctionInfo {
    public required SegmentedAddress Address { get; init; }

    public required string Name { get; init; }

    public required int CalledCount { get; init; }

    public required bool HasOverride { get; init; }
}
