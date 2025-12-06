namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record CfgCpuGraphResponse : McpToolResponse {
    public required int CurrentContextDepth { get; init; }

    public required string CurrentContextEntryPoint { get; init; }

    public required int TotalEntryPoints { get; init; }

    public required string[] EntryPointAddresses { get; init; }

    public required string LastExecutedAddress { get; init; }
}
