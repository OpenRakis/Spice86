namespace Spice86.Core.Emulator.Mcp.Response;

using Spice86.Core.Emulator.Devices.Sound.Midi;

internal sealed record MidiStateResponse {
    public required string DeviceKind { get; init; }

    public required bool UseMt32 { get; init; }

    public string? Mt32RomsPath { get; init; }

    public required GeneralMidiState State { get; init; }

    public required GeneralMidiStatus Status { get; init; }

    public required int DataPort { get; init; }

    public required int StatusPort { get; init; }
}