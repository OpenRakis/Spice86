namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record GusRegisterResponse
{
    public required int Voice { get; init; }

    public required int Register { get; init; }

    public required int Value { get; init; }
}
