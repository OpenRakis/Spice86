namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record IoPortReadResponse {
    public required int Port { get; init; }
    public required byte Value { get; init; }
}
