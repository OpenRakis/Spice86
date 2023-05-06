namespace Spice86.Shared.Emulator.Video;

/// <summary>
/// RGB representation of a color.
/// </summary>
/// <param name="R">Red channel</param>
/// <param name="G">Green channel</param>
/// <param name="B">Blue channel</param>
public readonly record struct Rgb(byte R, byte G, byte B) {
    /// <summary>
    /// Creates a new Rgb instance from a 32-bit unsigned integer value representing a color in the format 0xRRGGBB.
    /// </summary>
    /// <param name="value">The 32-bit unsigned integer value representing a color in the format 0xRRGGBB.</param>
    /// <returns>A new Rgb instance representing the color.</returns>
    public static Rgb FromUint(uint value) => new(
        (byte)((value >> 16) & 0xff),
        (byte)((value >> 8) & 0xff),
        (byte)(value & 0xff)
    );
}