namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record SoundBlasterMixerStateResponse {
    public required int MasterLeft { get; init; }

    public required int MasterRight { get; init; }

    public required int DacLeft { get; init; }

    public required int DacRight { get; init; }

    public required int FmLeft { get; init; }

    public required int FmRight { get; init; }

    public required int CdLeft { get; init; }

    public required int CdRight { get; init; }

    public required int LineInLeft { get; init; }

    public required int LineInRight { get; init; }

    public required int Microphone { get; init; }

    public required bool StereoOutputEnabled { get; init; }

    public required bool LowPassFilterEnabled { get; init; }
}