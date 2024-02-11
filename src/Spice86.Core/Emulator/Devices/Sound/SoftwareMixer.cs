namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Backend.Audio;

using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>
/// Basic software mixer for sound channels.
/// </summary>
internal class SoftwareMixer {
    private readonly List<SoundChannel> _channels = new();

    private readonly AudioPlayer _audioPlayer;

    public SoftwareMixer(AudioPlayerFactory audioPlayerFactory) {
        _audioPlayer = audioPlayerFactory.CreatePlayer();
    }

    public IReadOnlyCollection<SoundChannel> Channels => new ReadOnlyCollection<SoundChannel>(_channels);

    public void AddChannel(SoundChannel soundChannel) {
        _channels.Add(soundChannel);
    }

    public void RemoveChannel(SoundChannel soundChannel) {
        _channels.Remove(soundChannel);
    }

    public void Render(SoundChannel channel) {
        ApplyVolume(channel);
        ApplyStereoSeparation(channel);
        
        float[] buffer = new float[channel.AudioData.Length * 2];
        for (int i = 0; i < channel.AudioData.Length; i++) {
            buffer[i * 2] = channel.AudioData[i].Left;
            buffer[i * 2 + 1] = channel.AudioData[i].Right;
        }
        _audioPlayer.WriteFullBuffer(buffer);
    }

    private static void ApplyVolume(SoundChannel channel) {
        float volumeFactor = channel.Volume / 100f;
        for (int i = 0; i < channel.AudioData.Length; i++) {
            channel.AudioData[i].Left *= volumeFactor;
            channel.AudioData[i].Right *= volumeFactor;
        }
    }

    private static void ApplyStereoSeparation(SoundChannel channel) {
        float separation = channel.StereoSeparation / 100f;
        for (int i = 0; i < channel.AudioData.Length; i++) {
            channel.AudioData[i].Left *= (1 - separation);
            channel.AudioData[i].Right *= (1 + separation);
        }
    }
}
