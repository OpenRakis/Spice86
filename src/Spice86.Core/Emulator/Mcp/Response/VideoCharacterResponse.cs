namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record VideoCharacterResponse : McpToolResponse {
    public required int Page { get; init; }

    public required int X { get; init; }

    public required int Y { get; init; }

    public required string Character { get; init; }

    public required int Attribute { get; init; }

    public required bool UseAttribute { get; init; }
}