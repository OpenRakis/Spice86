namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record StackValue {
    public required uint Address { get; init; }

    public required ushort Value { get; init; }
}
