namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record MidiStateResponse : McpToolResponse {
    public required string DeviceKind { get; init; }

    public required bool UseMt32 { get; init; }

    public string? Mt32RomsPath { get; init; }

    public required string State { get; init; }

    public required int StatusValue { get; init; }

    public required bool InputReady { get; init; }

    public required bool OutputReady { get; init; }

    public required int DataPort { get; init; }

    public required int StatusPort { get; init; }
}