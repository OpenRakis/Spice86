namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Records;

/// <summary>
///     Represents a character with an attribute.
/// </summary>
public record struct CharacterPlusAttribute(char Character, byte Attribute, bool UseAttribute);