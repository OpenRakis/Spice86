namespace Spice86.Core.Emulator.Sound;
using System.Collections.Generic;

/// <summary>
/// Basic software mixer for sound channels.
/// </summary>
internal class SoftwareMixer {
    private readonly IDictionary<string, int> _channels = new Dictionary<string, int>();

    public SoftwareMixer() {
        _channels.Add("Master", 100);
    }

    public void AddChannel(string name, int volume) {
        _channels.Add(name, volume);
    }

    public void RemoveChannel(string name) {
        _channels.Remove(name);
    }

    public float[] Mix(params SoundChannel[] channels) {
        var result = new float[channels[0].AudioData.Length];
        foreach (SoundChannel channel in channels) {
            var volume = _channels[channel.Name];
            for (var i = 0; i < channel.AudioData.Length; i++) {
                result[i] += channel.AudioData[i] * volume / 100f;
            }
        }
        return result;
    }

    public void AdjustStereoSeparation(float separation, params SoundChannel[] channels) {
        foreach (SoundChannel channel in channels) {
            var volume = _channels[channel.Name];
            for (var i = 0; i < channel.AudioData.Length; i += 2) {
                channel.AudioData[i] = channel.AudioData[i] * (1 - separation);
                channel.AudioData[i + 1] = channel.AudioData[i + 1] * separation;
            }
        }
    }
}
