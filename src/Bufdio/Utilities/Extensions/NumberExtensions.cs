using System;

namespace Bufdio.Utilities.Extensions;

internal static class NumberExtensions
{
    public static TimeSpan Milliseconds(this double value)
    {
        return TimeSpan.FromMilliseconds(value);
    }

    public static float VerifyVolume(this float volume)
    {
        return volume switch
        {
            > 1.0f => 1.0f,
            < 0.0f => 0.0f,
            _ => volume
        };
    }
}
