namespace Spice86.Core.Backend.Audio;

using Spice86.Core.Emulator.Devices.Sound;

using System;

/// <summary>
/// Adapts channels of input audio data
/// </summary>
internal static class ChannelAdapter {
    /// <summary>
    /// Transforms mono audio data to stereo audio data.
    /// </summary>
    /// <param name="source">The source sample.</param>
    /// <param name="target">The target sample.</param>
    public static void MonoToStereo(ReadOnlySpan<MonoAudioFrame> source, Span<StereoAudioFrame> target) {
        for (int i = 0; i < target.Length; i++) {
            StereoAudioFrame element = target[i];
            element.Left = source[i].Frame[0];
            element.Right = source[i].Frame[0];
        }
    }

    /// <summary>
    /// Transforms mono audio data to stereo audio data.
    /// </summary>
    /// <param name="source">The source sample.</param>
    /// <param name="target">The target sample.</param>
    /// <typeparam name="TSample">The type of sample.</typeparam>
    /// <exception cref="ArgumentException">If the target length is lesser than double the source length</exception>
    public static void MonoToStereo<TSample>(ReadOnlySpan<TSample> source, Span<TSample> target) {
        if (target.Length < source.Length * 2) {
            throw new ArgumentException("Invalid target length.");
        }

        for (int i = 0; i < source.Length; i++) {
            int targetIndex = i * 2;
            target[targetIndex] = source[i];
            target[targetIndex + 1] = source[i];
        }
    }
}
