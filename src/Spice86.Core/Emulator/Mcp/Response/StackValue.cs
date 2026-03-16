namespace Spice86.Core.Emulator.Mcp.Response;

/// <summary>A single stack entry with its address and value.</summary>
public sealed record StackValue {
    public required uint Address { get; init; }
    public required ushort Value { get; init; }
}
