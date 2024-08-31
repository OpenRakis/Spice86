namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Backend.Audio;
using Spice86.Core.Backend.Audio.DummyAudio;
using Spice86.Core.Backend.Audio.PortAudio;
using Spice86.Shared.Interfaces;

/// <summary>
/// Provides methods to create an audio player
/// </summary>
public class AudioPlayerFactory {
    private readonly PortAudioPlayerFactory _portAudioPlayerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioPlayerFactory"/> class.
    /// </summary>
    /// <param name="loggerService">The logger service.</param>
    public AudioPlayerFactory(ILoggerService loggerService) {
        _portAudioPlayerFactory = new(loggerService);
    }

    /// <summary>
    /// Creates an instance of an <see cref="AudioPlayer"/> with the specified sample rate, frames per buffer, and suggested latency.
    /// PortAudio is the default but if for some reason it cannot be loaded, return a dummy player that does not output anything.
    /// </summary>
    /// <param name="sampleRate">The sample rate of the audio player, in Hz.</param>
    /// <param name="framesPerBuffer">The number of frames per buffer, or 0 for the default value.</param>
    /// <param name="suggestedLatency">The suggested latency of the audio player, or null for the default value.</param>
    /// <returns>An instance of an <see cref="AudioPlayer"/>.</returns>
    public AudioPlayer CreatePlayer(int sampleRate = 48000, int framesPerBuffer = 0,
        double? suggestedLatency = null) {
        AudioPlayer? res = _portAudioPlayerFactory.Create(sampleRate, framesPerBuffer, suggestedLatency);
        if (res != null) {
            return res;
        }

        return new DummyAudioPlayer(new AudioFormat(SampleRate: sampleRate, Channels: 2,
            SampleFormat: SampleFormat.IeeeFloat32));
    }
}
