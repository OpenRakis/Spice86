namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Backend.Audio;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;

/// <summary>
/// Basic software mixer for sound channels.
/// </summary>
public sealed class SoftwareMixer : IDisposable {
    
    private readonly List<SoundChannel> _channels = new();

    private readonly AudioPlayer _audioPlayer;

    private bool _disposed;

    public SoftwareMixer(AudioPlayerFactory audioPlayerFactory) {
        _audioPlayer = audioPlayerFactory.CreatePlayer();
    }

    public IReadOnlyCollection<SoundChannel> Channels => new ReadOnlyCollection<SoundChannel>(_channels);

    internal SoundChannel CreateSoundChannel(string name) {
        SoundChannel soundChannel = new(this)
        {
            Name = name
        };
        _channels.Add(soundChannel);
        return soundChannel;
    }

    internal void Render(SoundChannel channel) {
        ApplyVolume(channel);
        ApplyStereoSeparation(channel);
        _audioPlayer.WriteFullBuffer(channel.Data.Frame);
    }

    private static void ApplyVolume(SoundChannel channel) {
        float volumeFactor = channel.Volume / 100f;
        for (int i = 0; i < channel.Data.Frame.Length; i++) {
            StereoAudioFrame stereoAudioFrame = channel.Data;
            stereoAudioFrame.Left *= volumeFactor;
            stereoAudioFrame.Right *= volumeFactor;
        }
    }

    private static void ApplyStereoSeparation(SoundChannel channel) {
        float separation = channel.StereoSeparation / 100f;
        for (int i = 0; i < channel.Data.Frame.Length; i++) {
            StereoAudioFrame stereoAudioFrame = channel.Data;
            stereoAudioFrame.Left *= (1 - separation);
            stereoAudioFrame.Right *= (1 + separation);
        }
    }

    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _audioPlayer.Dispose();
            }
            _disposed = true;
        }
    }
    public void Dispose() {
        Dispose(true);
    }
}
