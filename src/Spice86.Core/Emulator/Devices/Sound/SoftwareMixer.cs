namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Backend.Audio;
using Spice86.Core.Emulator.InternalDebugger;

using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>
/// Basic software mixer for sound channels.
/// </summary>
public sealed class SoftwareMixer : IDisposable, IDebuggableComponent {
    private readonly Dictionary<SoundChannel, AudioPlayer> _channels = new();
    private readonly AudioPlayerFactory _audioPlayerFactory;
    private ReadOnlyDictionary<SoundChannel, AudioPlayer> _channelsReadOnlyDictionary = new(new Dictionary<SoundChannel, AudioPlayer>());
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SoftwareMixer"/> class.
    /// </summary>
    /// <param name="audioPlayerFactory">The factory for creating an audio player for each new sound channel.</param>
    public SoftwareMixer(AudioPlayerFactory audioPlayerFactory) {
        _audioPlayerFactory = audioPlayerFactory;
    }

    internal void Register(SoundChannel soundChannel) {
        _channels.Add(soundChannel, _audioPlayerFactory.CreatePlayer());
        _channelsReadOnlyDictionary = new(_channels);
    }


    /// <summary>
    /// Gets the sound channels in a read-only dictionary.
    /// </summary>
    public IDictionary<SoundChannel, AudioPlayer> Channels => _channelsReadOnlyDictionary;

    internal int Render(Span<float> data, SoundChannel channel) {
        if (channel.Volume == 0 || channel.IsMuted) {
            _channels[channel].WriteSilence();
            return data.Length;
        }
        ApplyStereoSeparation(data, channel);
        ApplyVolume(data, channel);
        return _channels[channel].WriteData(data);
    }

    internal int Render(Span<int> data, SoundChannel channel) {
        if (channel.Volume == 0 || channel.IsMuted) {
            _channels[channel].WriteSilence();
            return data.Length;
        }
        ApplyStereoSeparation(data, channel);
        ApplyVolume(data, channel);
        return _channels[channel].WriteData(data);
    }

    internal int Render(Span<short> data, SoundChannel channel) {
        if (channel.Volume == 0 || channel.IsMuted) {
            _channels[channel].WriteSilence();
            return data.Length;
        }
        ApplyStereoSeparation(data, channel);
        ApplyVolume(data, channel);
        return _channels[channel].WriteData(data);
    }
    
    internal int Render(Span<byte> data, SoundChannel channel) {
        if (channel.Volume == 0 || channel.IsMuted) {
            _channels[channel].WriteSilence();
            return data.Length;
        }
        ApplyStereoSeparation(data, channel);
        ApplyVolume(data, channel);
        return _channels[channel].WriteData(data);
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

    /// <inheritdoc/>
    public void Accept<T>(T emulatorDebugger) where T : IInternalDebugger {
        emulatorDebugger.Visit(this);
    }
    
    private static void ApplyVolume(Span<float> data, SoundChannel channel) {
        float volumeFactor = channel.Volume / 100f;
        for(int i = 0; i < data.Length; i++) {
            data[i] *= volumeFactor;
        }
    }

    private static void ApplyVolume(Span<int> data, SoundChannel channel) {
        float volumeFactor = channel.Volume / 100f;
        for(int i = 0; i < data.Length; i++) {
            data[i] = (int)(data[i] * volumeFactor);
        }
    }

    private static void ApplyVolume(Span<short> data, SoundChannel channel) {
        float volumeFactor = channel.Volume / 100f;
        for(int i = 0; i < data.Length; i++) {
            data[i] = (short)(data[i] * volumeFactor);
        }
    }
    
    private static void ApplyVolume(Span<byte> data, SoundChannel channel) {
        float volumeFactor = channel.Volume / 100f;
        for(int i = 0; i < data.Length; i++) {
            data[i] = (byte)(data[i] * volumeFactor);
        }
    }

    private static void ApplyStereoSeparation(Span<float> data, SoundChannel channel) {
        float separation = channel.StereoSeparation / 100f;
        for(int i = 0; i < data.Length; i++) {
            data[i] *= (1 - separation);
        }
    }

    private static void ApplyStereoSeparation(Span<int> data, SoundChannel channel) {
        float separation = channel.StereoSeparation / 100f;
        for(int i = 0; i < data.Length; i++) {
            data[i] = (int)(data[i] * (1 - separation));
        }
    }

    private static void ApplyStereoSeparation(Span<short> data, SoundChannel channel) {
        float separation = channel.StereoSeparation / 100f;
        for(int i = 0; i < data.Length; i++) {
            data[i] = (short)(data[i] * (1 - separation));
        }
    }
    
    private static void ApplyStereoSeparation(Span<byte> data, SoundChannel channel) {
        float separation = channel.StereoSeparation / 100f;
        for(int i = 0; i < data.Length; i++) {
            data[i] = (byte)(data[i] * (1 - separation));
        }
    }
}
