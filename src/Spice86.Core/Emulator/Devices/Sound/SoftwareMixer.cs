namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Backend.Audio;
using Spice86.Shared.Interfaces;

using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>
/// Basic software mixer for sound channels.
/// </summary>
public sealed class SoftwareMixer : IDisposable {
    private readonly Dictionary<SoundChannel, AudioPlayer> _channels = new();
    private readonly AudioPlayerFactory _audioPlayerFactory;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SoftwareMixer"/> class.
    /// </summary>
    /// <param name="loggerService">The logger service.</param>
    /// <param name="audioEngine">Audio engine to use.</param>
    public SoftwareMixer(ILoggerService loggerService, AudioEngine audioEngine) {
        _audioPlayerFactory = new(loggerService, audioEngine);
    }

    internal SoundChannel CreateChannel(string name) {
        SoundChannel soundChannel = new(this, name);
        return soundChannel;
    }

    internal void Register(SoundChannel soundChannel) {
        _channels.Add(soundChannel, _audioPlayerFactory.CreatePlayer(sampleRate: 48000, framesPerBuffer: 2048));
        Channels = _channels.AsReadOnly();
    }
    
    /// <summary>
    /// Gets the sound channels in a read-only dictionary.
    /// </summary>
    public IReadOnlyDictionary<SoundChannel, AudioPlayer> Channels { get; private set; } = new Dictionary<SoundChannel, AudioPlayer>().AsReadOnly();

    internal int Render(Span<float> data, SoundChannel channel) {
        if (channel.Volume == 0 || channel.IsMuted) {
            _channels[channel].WriteSilence();
            return data.Length;
        }
        float volumeFactor = channel.Volume / 100f;
        float separation = channel.StereoSeparation / 100f;
        float finalVolumeFactor = volumeFactor * (1 + separation);
        
        Span<float> target = stackalloc float[data.Length];
        for (int i = 0; i < data.Length; i++) {
            target[i] = data[i] * finalVolumeFactor;
        }
        return _channels[channel].WriteData(target);
    }
    
    internal int Render(Span<short> data, SoundChannel channel) {
        if (channel.Volume == 0 || channel.IsMuted) {
            _channels[channel].WriteSilence();
            return data.Length;
        }
        float volumeFactor = channel.Volume / 100f;
        float separation = channel.StereoSeparation / 100f;
        float finalVolumeFactor = volumeFactor * (1 + separation);

        Span<float> target = stackalloc float[data.Length];
        for (int i = 0; i < data.Length; i++) {
            target[i] = (data[i] / 32768f) * finalVolumeFactor;
        }

        return _channels[channel].WriteData(target);
    }
    
    internal int Render(Span<byte> data, SoundChannel channel) {
        if (channel.Volume == 0 || channel.IsMuted) {
            _channels[channel].WriteSilence();
            return data.Length;
        }
        float volumeFactor = channel.Volume / 100f;
        float separation = channel.StereoSeparation / 100f;
        float finalVolumeFactor = volumeFactor * (1 + separation);
        
        Span<float> target = stackalloc float[data.Length];
        for (int i = 0; i < data.Length; i++) {
            target[i] = ((data[i] - 127) / 128f) * finalVolumeFactor;
        }
        return _channels[channel].WriteData(target);
    }
    
    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                foreach (AudioPlayer audioPlayer in _channels.Values) {
                    audioPlayer.Dispose();
                }
                _channels.Clear();
            }
            _disposed = true;
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        Dispose(true);
    }
}