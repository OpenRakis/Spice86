namespace Spice86.Core.Emulator.Mcp.Response;

using Spice86.Shared.Emulator.Memory;

internal sealed record CfgCpuGraphResponse {
    public required int CurrentContextDepth { get; init; }

    public required SegmentedAddress CurrentContextEntryPoint { get; init; }

    public required int TotalEntryPoints { get; init; }

    public required SegmentedAddress[] EntryPointAddresses { get; init; }

    public required SegmentedAddress? LastExecutedAddress { get; init; }

    public required CfgNodeInfo[] Nodes { get; init; }

    public required bool Truncated { get; init; }
}
