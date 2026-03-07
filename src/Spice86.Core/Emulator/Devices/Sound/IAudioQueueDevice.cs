namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Audio.Backend;

/// <summary>
/// Interface for audio devices that produce audio on the main thread and
/// consume on the mixer thread using a queue-based callback pattern.
/// </summary>
/// <remarks>
/// 2022-2025 The DOSBox Staging Team
/// </remarks>
/// <typeparam name="T">The audio sample type (typically float or AudioFrame).</typeparam>
public interface IAudioQueueDevice<T> where T : struct {
    /// <summary>
    /// Gets the output queue containing audio samples produced by this device.
    /// </summary>
    RWQueue<T> OutputQueue { get; }

    /// <summary>
    /// Gets the mixer channel associated with this device.
    /// </summary>
    SoundChannel Channel { get; }
}
