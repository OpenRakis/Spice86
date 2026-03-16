namespace Spice86.Core.Emulator.Devices.Sound.AdlibGoldOpl;

/// <summary>
///     Represents the packed switch-function register format.
/// </summary>
internal readonly struct StereoProcessorSwitchFunctions(byte data) {
    /// <summary>
    ///     Gets the encoded source selector value.
    /// </summary>
    internal byte SourceSelector => (byte)(data & 0x07);

    /// <summary>
    ///     Gets the encoded stereo mode value.
    /// </summary>
    internal byte StereoMode => (byte)((data >> 3) & 0x03);

    /// <summary>
    ///     Combines the source selector and stereo mode into a packed byte.
    /// </summary>
    internal static byte Compose(byte sourceSelector, byte stereoMode) {
        return (byte)((sourceSelector & 0x07) | ((stereoMode & 0x03) << 3));
    }
}
