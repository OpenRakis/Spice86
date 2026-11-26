namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

/// <summary>
///     Registers of the Sound Blaster's mixer chip, as captured by <see cref="SoundBlaster.CaptureState" />.
/// </summary>
/// <remarks>
///     Each volume holds a left and a right level.
/// </remarks>
/// <param name="Index">Register selected through the mixer index port.</param>
/// <param name="Dac">Digital audio volume.</param>
/// <param name="Fm">FM synthesis volume.</param>
/// <param name="CdAudio">CD audio volume.</param>
/// <param name="Master">Master volume.</param>
/// <param name="LineIn">Line in volume.</param>
/// <param name="Mic">Microphone volume.</param>
/// <param name="StereoEnabled">Whether the output stereo bit is set.</param>
/// <param name="FilterEnabled">Whether the output filter bit selects the filter.</param>
/// <param name="Unhandled">Values written to registers the mixer does not emulate, read back unchanged.</param>
/// <param name="EssIdentificationPosition">Position in the ESS identification string.</param>
public sealed record SoundBlasterMixerState(
    byte Index,
    ReadOnlyMemory<byte> Dac,
    ReadOnlyMemory<byte> Fm,
    ReadOnlyMemory<byte> CdAudio,
    ReadOnlyMemory<byte> Master,
    ReadOnlyMemory<byte> LineIn,
    byte Mic,
    bool StereoEnabled,
    bool FilterEnabled,
    ReadOnlyMemory<byte> Unhandled,
    byte EssIdentificationPosition);
