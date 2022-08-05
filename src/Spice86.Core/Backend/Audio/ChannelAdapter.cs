namespace Spice86.Core.Backend.Audio;

using System;

public static class ChannelAdapter {
    public static void MonoToStereo<TSample>(ReadOnlySpan<TSample> source, Span<TSample> target) {
        if (target.Length < source.Length * 2)
            throw new ArgumentException("Invalid target length.");

        for (int i = 0; i < source.Length; i++) {
            int targetIndex = i * 2;
            target[targetIndex] = source[i];
            target[targetIndex + 1] = source[i];
        }
    }
}
