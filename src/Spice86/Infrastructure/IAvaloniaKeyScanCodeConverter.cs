namespace Spice86.Infrastructure;

using Spice86.Shared.Emulator.Keyboard;

/// <summary>
/// A utility class that provides mapping from AvaloniaUI <see cref="Key"/> values to keyboard scan codes.
/// </summary>
public interface IAvaloniaKeyScanCodeConverter {

    /// <summary>
    /// Gets the ASCII code from the input scancode.
    /// </summary>
    /// <param name="keyPressedScanCode">The scancode of the pressed keyboard key</param>
    /// <returns>The corresponding ASCII code, or <c>null</c> if not found.</returns>
    byte? GetAsciiCode(byte? keyPressedScanCode);

    /// <summary>
    /// Retrieves the scancode of a pressed key, if it exists in the _keyPressedScanCode dictionary.
    /// </summary>
    /// <param name="key">The key for which to retrieve the scancode.</param>
    /// <returns>The scancode of the pressed key, or null if the key is not present in the dictionary.</returns>
    byte? GetKeyPressedScancode(Key key);

    /// <summary>
    /// Retrieves the scancode of a released key, if it exists in the _keyPressedScanCode dictionary.
    /// </summary>
    /// <param name="key">The key for which to retrieve the scancode.</param>
    /// <returns>The scancode of the released key, or null if the key is not present in the dictionary.</returns>
    byte? GetKeyReleasedScancode(Key key);
}