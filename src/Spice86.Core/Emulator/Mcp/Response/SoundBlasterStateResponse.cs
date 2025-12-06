namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record SoundBlasterStateResponse : McpToolResponse {
    public required string SbType { get; init; }

    public required int BaseAddress { get; init; }

    public required int Irq { get; init; }

    public required int LowDma { get; init; }

    public required int HighDma { get; init; }

    public required string BlasterString { get; init; }

    public required bool SpeakerEnabled { get; init; }

    public required uint DspFrequencyHz { get; init; }

    public required int DspTestRegister { get; init; }
}