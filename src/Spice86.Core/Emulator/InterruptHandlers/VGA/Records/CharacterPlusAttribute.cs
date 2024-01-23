namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Records;

/// <summary>
///     Represents a character with an attribute.
/// </summary>
/// <param name="Character">The ASCII code of a character</param>
/// <param name="Attribute">The byte that describes color, blinking and charset</param>
/// <param name="UseAttribute">Whether the attribute needs to be used</param>
public readonly record struct CharacterPlusAttribute(char Character, byte Attribute, bool UseAttribute);