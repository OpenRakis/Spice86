namespace Spice86.Shared.Emulator.Audio;
using System;
using System.Collections.Generic;

/// <summary>
/// Static class with methods to transform <see cref="Span{T}"/> to <see cref="List{T}"/>.
/// </summary>
public static class AudioSpanExtensions
{
    /// <summary>
    /// Transforms a <see cref="Span{T}"/> to a list of <see cref="AudioFrame{T}"/> with a left and a right channel.
    /// </summary>
    /// <typeparam name="T">int, float, short, or other struct</typeparam>
    /// <param name="span">The input span. The number of elements can be odd.</param>
    /// <returns>A list of <see cref="AudioFrame{T}"/>s</returns>
    public static List<AudioFrame<T>> ToAudioFrames<T>(this Span<T> span) where T : struct
    {
        var frames = new List<AudioFrame<T>>();
        for (int index = 0; index < span.Length; index += 2)
        {
            int left = index;
            int right = index + 1 < span.Length ? index + 1 : index;
            frames.Add(new AudioFrame<T>
            {
                Left = span[left],
                Right = span[right]
            });
        }
        return frames;
    }
}
