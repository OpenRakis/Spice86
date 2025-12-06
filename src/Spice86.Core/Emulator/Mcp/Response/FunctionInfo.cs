namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record FunctionInfo {
    public required string Address { get; init; }

    public required string Name { get; init; }

    public required int CalledCount { get; init; }

    public required bool HasOverride { get; init; }
}
