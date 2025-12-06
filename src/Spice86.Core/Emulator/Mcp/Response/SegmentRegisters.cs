namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record SegmentRegisters {
    public required ushort CS { get; init; }

    public required ushort DS { get; init; }

    public required ushort ES { get; init; }

    public required ushort FS { get; init; }

    public required ushort GS { get; init; }

    public required ushort SS { get; init; }
}
