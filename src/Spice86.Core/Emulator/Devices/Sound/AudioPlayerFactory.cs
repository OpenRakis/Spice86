namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Backend.Audio;
using Spice86.Core.Backend.Audio.CrossPlatform;
using Spice86.Core.Backend.Audio.DummyAudio;
using Spice86.Shared.Interfaces;

/// <summary>
/// Provides methods to create an audio player.
/// </summary>
public class AudioPlayerFactory {
    private readonly ILoggerService _loggerService;
    private readonly AudioEngine _audioEngine;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioPlayerFactory"/> class.
    /// </summary>
    /// <param name="loggerService">The logger service.</param>
    /// <param name="audioEngine">Audio engine to use.</param>
    public AudioPlayerFactory(ILoggerService loggerService, AudioEngine audioEngine) {
        _loggerService = loggerService;
        _audioEngine = audioEngine;
    }

    /// <summary>
    /// Creates an instance of an <see cref="AudioPlayer"/> with the specified sample rate and frames per buffer.
    /// Uses SDL on all platforms to keep audio behavior consistent.
    /// Falls back to a dummy engine if the native backend cannot be initialized.
    /// </summary>
    /// <param name="sampleRate">The sample rate of the audio player, in Hz.</param>
    /// <param name="framesPerBuffer">The number of frames per buffer, or 0 for the default value.</param>
    /// <param name="prebufferMs">Prebuffer duration in milliseconds.</param>
    /// <returns>An instance of an <see cref="AudioPlayer"/>.</returns>
    public AudioPlayer CreatePlayer(int sampleRate, int framesPerBuffer, int prebufferMs) {
        if (_audioEngine == AudioEngine.CrossPlatform) {
            AudioPlayer? player = TryCreateCrossPlatformPlayer(sampleRate, framesPerBuffer, prebufferMs);
            if (player != null) {
                return player;
            }
            _loggerService.Warning("Failed to initialize cross-platform audio backend, falling back to dummy audio");
        }

        return new DummyAudioPlayer(new AudioFormat(SampleRate: sampleRate, Channels: 2,
            SampleFormat: SampleFormat.IeeeFloat32));
    }

    private CrossPlatformAudioPlayer? TryCreateCrossPlatformPlayer(int sampleRate, int framesPerBuffer, int prebufferMs) {
        try {
            IAudioBackend? backend = AudioBackendFactory.Create();
            if (backend == null) {
                _loggerService.Warning("No audio backend available for current platform");
                return null;
            }

            int bufferFrames = framesPerBuffer > 0 ? framesPerBuffer : 1024;
            AudioFormat format = new AudioFormat(SampleRate: sampleRate, Channels: 2,
                SampleFormat: SampleFormat.IeeeFloat32);

            return new CrossPlatformAudioPlayer(format, backend, bufferFrames, prebufferMs);
        } catch (InvalidOperationException ex) {
            _loggerService.Warning("Failed to create cross-platform audio player: {Error}", ex.Message);
            return null;
        }
    }
}