namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Shared.Utils;

/// <summary>
/// Interface for audio devices that produce audio on the main thread and
/// consume on the mixer thread using a queue-based callback pattern.
/// Reference: DOSBox's MIXER_PullFromQueueCallback template pattern.
/// </summary>
/// <typeparam name="T">The audio sample type (typically float).</typeparam>
public interface IAudioQueueDevice<T> {
    /// <summary>
    /// Gets the output queue containing audio samples produced by this device.
    /// </summary>
    RWQueue<T> OutputQueue { get; }

    /// <summary>
    /// Gets the mixer channel associated with this device.
    /// </summary>
    MixerChannel Channel { get; }
}
