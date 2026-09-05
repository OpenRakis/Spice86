namespace Spice86.Core.Emulator.Mcp.Response;

using System.Collections.Generic;

internal sealed record GusVoicesResponse
{
    public required int StartVoice { get; init; }

    public required int Count { get; init; }

    public required int ActiveVoices { get; init; }

    public required IReadOnlyList<GusVoiceStateResponse> Voices { get; init; }
}
