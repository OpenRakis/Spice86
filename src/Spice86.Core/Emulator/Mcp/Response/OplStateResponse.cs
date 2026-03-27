namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record OplStateResponse {
    public required string Mode { get; init; }

    public required bool AdlibGoldEnabled { get; init; }

    public required string MixerChannelName { get; init; }

    public required int MixerChannelSampleRate { get; init; }

    public required bool MixerChannelEnabled { get; init; }
}