namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record BiosDataAreaResponse {
    public required int ConventionalMemorySizeKb { get; init; }

    public required int EquipmentListFlags { get; init; }

    public required int VideoMode { get; init; }

    public required int ScreenColumns { get; init; }

    public required int ScreenRows { get; init; }

    public required int CurrentVideoPage { get; init; }

    public required int CharacterHeight { get; init; }

    public required int CrtControllerBaseAddress { get; init; }

    public required uint TimerCounter { get; init; }

    public required int LastUnexpectedIrq { get; init; }
}