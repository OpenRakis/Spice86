namespace Spice86.Core.Emulator.Mcp.Response;

public sealed record StackValue {
    public required uint Address { get; init; }
    public required ushort Value { get; init; }
}

public sealed record StackResponse : McpToolResponse {
    public required List<StackValue> Values { get; init; }
    public required uint Ss { get; init; }
    public required uint Sp { get; init; }
}
