namespace Spice86.Core.Emulator.Mcp.Response;

using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;

internal sealed record VideoCharacterResponse {
    public required CursorPosition Position { get; init; }

    public required CharacterPlusAttribute Character { get; init; }
}
