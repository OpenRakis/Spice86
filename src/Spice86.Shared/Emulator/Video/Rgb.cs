namespace Spice86.Shared.Emulator.Video;

/// <summary>
/// RGB representation of a color.
/// </summary>
/// <param name="R">Red channel</param>
/// <param name="G">Green channel</param>
/// <param name="B">Blue channel</param>
public readonly record struct Rgb(byte R, byte G, byte B) {
    public static Rgb FromUint(uint value) => new(
        (byte)((value >> 16) & 0xff),
        (byte)((value >> 8) & 0xff),
        (byte)(value & 0xff)
    );
}