namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record GusTimerStateResponse
{
    public required int Index { get; init; }

    public required double DelayMs { get; init; }

    public required byte Value { get; init; }

    public required bool HasExpired { get; init; }

    public required bool IsCountingDown { get; init; }

    public required bool IsMasked { get; init; }

    public required bool ShouldRaiseIrq { get; init; }
}
