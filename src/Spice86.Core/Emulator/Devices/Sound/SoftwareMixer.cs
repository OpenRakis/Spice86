namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Backend.Audio;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
///     Basic software mixer for sound channels.
/// </summary>
public sealed class SoftwareMixer : IDisposable {
    private readonly AudioPlayerFactory _audioPlayerFactory;
    private readonly Dictionary<SoundChannel, AudioPlayer> _channels = new();
    private readonly ILoggerService _logger;
    private bool _disposed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SoftwareMixer" /> class.
    /// </summary>
    /// <param name="loggerService">The logger service.</param>
    /// <param name="audioEngine">Audio engine to use.</param>
    public SoftwareMixer(ILoggerService loggerService, AudioEngine audioEngine) {
        _logger = loggerService ?? throw new ArgumentNullException(nameof(loggerService));
        _audioPlayerFactory = new AudioPlayerFactory(_logger, audioEngine);
    }

    /// <summary>
    ///     Gets the sound channels in a read-only dictionary.
    /// </summary>
    public IReadOnlyDictionary<SoundChannel, AudioPlayer> Channels { get; private set; } =
        new Dictionary<SoundChannel, AudioPlayer>().AsReadOnly();

    /// <inheritdoc />
    public void Dispose() {
        Dispose(true);
    }

    /// <summary>
    ///     Creates a new <see cref="SoundChannel" /> backed by this mixer.
    /// </summary>
    /// <param name="name">The unique name of the channel.</param>
    /// <param name="sampleRate">Optional sample rate, in Hz.</param>
    /// <returns>The newly created channel.</returns>
    internal SoundChannel CreateChannel(string name, int sampleRate = 48000) {
        return new SoundChannel(this, _logger, name, sampleRate);
    }

    /// <summary>
    ///     Registers the supplied <see cref="SoundChannel" /> with the mixer and creates its audio player.
    /// </summary>
    /// <param name="soundChannel">The channel instance to register.</param>
    /// <param name="sampleRate">The channel sample rate, in Hz.</param>
    internal void Register(SoundChannel soundChannel, int sampleRate) {
        _channels.Add(soundChannel, _audioPlayerFactory.CreatePlayer(sampleRate, 2048));
        Channels = _channels.AsReadOnly();

        _logger.Debug("SOFTWARE MIXER: Registered channel {ChannelName} at sample rate {SampleRate} Hz.",
            soundChannel.Name, sampleRate);
    }

    /// <summary>
    ///     Mixes 32-bit floating point audio samples into the underlying player.
    /// </summary>
    /// <param name="data">The audio data to mix.</param>
    /// <param name="channel">The channel associated with the audio data.</param>
    /// <returns>The number of samples written.</returns>
    internal void Render(Span<float> data, SoundChannel channel) {
        if (channel.Volume == 0 || channel.IsMuted) {
            _channels[channel].WriteSilence();
            return;
        }

        float volumeFactor = channel.Volume / 100f;
        float separation = channel.StereoSeparation / 100f;
        float finalVolumeFactor = volumeFactor * (1 + separation);

        Span<float> target = stackalloc float[data.Length];
        data.CopyTo(target);
        SimdConversions.ScaleInPlace(target, finalVolumeFactor);
        
        _channels[channel].WriteData(target);
    }

    /// <summary>
    ///     Mixes 16-bit signed integer audio samples into the underlying player.
    /// </summary>
    /// <param name="data">The audio data to mix.</param>
    /// <param name="channel">The channel associated with the audio data.</param>
    /// <returns>The number of samples written.</returns>
    internal void Render(Span<short> data, SoundChannel channel) {
        if (channel.Volume == 0 || channel.IsMuted) {
            _channels[channel].WriteSilence();
            return;
        }

        float volumeFactor = channel.Volume / 100f;
        float separation = channel.StereoSeparation / 100f;
        float finalVolumeFactor = volumeFactor * (1 + separation);
        
        Span<float> target = stackalloc float[data.Length];
        float scale = finalVolumeFactor / 32768f;
        SimdConversions.ConvertInt16ToScaledFloat(data, target, scale);

        _channels[channel].WriteData(target);
    }

    /// <summary>
    ///     Mixes 8-bit unsigned integer audio samples into the underlying player.
    /// </summary>
    /// <param name="data">The audio data to mix.</param>
    /// <param name="channel">The channel associated with the audio data.</param>
    /// <returns>The number of samples written.</returns>
    internal void Render(Span<byte> data, SoundChannel channel) {
        if (channel.Volume == 0 || channel.IsMuted) {
            _channels[channel].WriteSilence();
            return;
        }

        float volumeFactor = channel.Volume / 100f;
        float separation = channel.StereoSeparation / 100f;
        float finalVolumeFactor = volumeFactor * (1 + separation);

        Span<float> target = stackalloc float[data.Length];
        SimdConversions.ConvertUInt8ToScaledFloat(data, target, finalVolumeFactor);

        _channels[channel].WriteData(target);
    }

    /// <summary>
    ///     Disposes the mixer, releasing audio players and clearing the channel registry.
    /// </summary>
    /// <param name="disposing">Indicates whether managed resources should be released.</param>
    private void Dispose(bool disposing) {
        if (_disposed) {
            return;
        }

        _logger.Debug("SOFTWARE MIXER: Disposing mixer (disposing = {Disposing}).", disposing);

        if (disposing) {
            foreach ((SoundChannel channel, AudioPlayer audioPlayer) in _channels) {
                try {
                    audioPlayer.Dispose();
                } catch (Exception ex) {
                    _logger.Error(ex, "SOFTWARE MIXER: Failed to dispose audio player for channel {ChannelName}.",
                        channel.Name);
                    throw;
                }
            }

            _channels.Clear();
        }

        _disposed = true;
    }
}