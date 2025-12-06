namespace Spice86.Core.Emulator.Mcp.Response;

using System.Collections.Generic;

internal sealed record VideoPaletteStateResponse : McpToolResponse {
    public required IReadOnlyList<int> Registers { get; init; }

    public required int OverscanBorderColor { get; init; }

    public required int PixelMask { get; init; }

    public required int ColorPageState { get; init; }
}