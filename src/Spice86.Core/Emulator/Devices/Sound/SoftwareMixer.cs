namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Backend.Audio;
using Spice86.Core.Emulator.InternalDebugger;

using System.Collections.Frozen;
using System.Collections.Generic;

/// <summary>
/// Basic software mixer for sound channels.
/// </summary>
public sealed class SoftwareMixer : IDisposable, IDebuggableComponent {
    private readonly Dictionary<SoundChannel, AudioPlayer> _channels = new();
    private readonly AudioPlayerFactory _audioPlayerFactory;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SoftwareMixer"/> class.
    /// </summary>
    /// <param name="audioPlayerFactory">The factory for creating an audio player for each new sound channel.</param>
    public SoftwareMixer(AudioPlayerFactory audioPlayerFactory) {
        _audioPlayerFactory = audioPlayerFactory;
    }

    internal void Register(SoundChannel soundChannel) {
        _channels.Add(soundChannel, _audioPlayerFactory.CreatePlayer(sampleRate: 48000, framesPerBuffer: 2048));
        Channels = _channels.ToFrozenDictionary();
    }
    
    /// <summary>
    /// Gets the sound channels in a read-only dictionary.
    /// </summary>
    public FrozenDictionary<SoundChannel, AudioPlayer> Channels { get; private set; } = new Dictionary<SoundChannel, AudioPlayer>().ToFrozenDictionary();

    internal int Render(Span<float> data, SoundChannel channel) {
        if (channel.Volume == 0 || channel.IsMuted) {
            _channels[channel].WriteSilence();
            return data.Length;
        }
        float volumeFactor = channel.Volume / 100f;
        float separation = channel.StereoSeparation / 100f;
        
        Span<float> target = stackalloc float[data.Length];
        for (int i = 0; i < data.Length; i++) {
            target[i] = data[i]  * volumeFactor * (1 - separation);
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

        Span<float> target = stackalloc float[data.Length];
        for (int i = 0; i < data.Length; i++) {
            target[i] = (data[i] / 32768f)  * volumeFactor * (1 - separation);
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
        
        Span<float> target = stackalloc float[data.Length];
        for (int i = 0; i < data.Length; i++) {
            target[i] = ((data[i] - 127) / 128f)  * volumeFactor * (1 - separation);
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

    /// <inheritdoc/>
    public void Accept<T>(T emulatorDebugger) where T : IInternalDebugger {
        emulatorDebugger.Visit(this);
    }
}
