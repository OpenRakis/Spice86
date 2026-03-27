namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record PcSpeakerStateResponse {
    public required int ControlPort { get; init; }

    public required int ControlValue { get; init; }

    public required bool Timer2GateEnabled { get; init; }

    public required bool SpeakerOutputEnabled { get; init; }

    public required bool Timer2OutputHigh { get; init; }

    public required string MixerChannelName { get; init; }

    public required int MixerChannelSampleRate { get; init; }

    public required bool MixerChannelEnabled { get; init; }
}