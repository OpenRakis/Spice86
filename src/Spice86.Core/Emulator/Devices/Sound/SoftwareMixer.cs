namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Backend.Audio;

using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>
/// Basic software mixer for sound channels.
/// </summary>
public sealed class SoftwareMixer : IDisposable {
    private readonly Dictionary<SoundChannel, AudioPlayer> _channels = new();
    private readonly AudioPlayerFactory _audioPlayerFactory;
    private bool _disposed;

    public SoftwareMixer(AudioPlayerFactory audioPlayerFactory) {
        _audioPlayerFactory = audioPlayerFactory;
    }
    
    internal void Register(SoundChannel soundChannel) {
        _channels.Add(soundChannel, _audioPlayerFactory.CreatePlayer());
    }

    public IDictionary<SoundChannel, AudioPlayer> Channels => new ReadOnlyDictionary<SoundChannel, AudioPlayer>(_channels);
    
    internal void Render(Span<float> buffer, SoundChannel channel) {
        if (channel.Volume == 0 || channel.IsMuted) {
            return;
        }
        ApplyStereoSeparation(buffer, channel);
        ApplyVolume(buffer, channel);
        _channels[channel].WriteFullBuffer(buffer);
    }

    private static void ApplyVolume(Span<float> buffer, SoundChannel channel) {
        float volumeFactor = channel.Volume / 100f;
        for (int i = 0; i < buffer.Length; i++) {
            float sample = buffer[i];
            sample *= volumeFactor;
            buffer[i] = sample;
        }
    }

    private static void ApplyStereoSeparation(Span<float> buffer, SoundChannel channel) {
        float separation = channel.StereoSeparation / 100f;
        for (int i = 0; i < buffer.Length; i++) {
            float sample = buffer[i];
            sample *= (1 + separation);
            buffer[i] = sample;
        }
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

    public void Dispose() {
        Dispose(true);
    }
}
