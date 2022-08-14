using System;
using Bufdio.Utilities.Extensions;

namespace Bufdio.Exceptions;

/// <summary>
/// An exception that is thrown when errors occured in internal FFmpeg processes.
/// <para>Implements: <see cref="Exception"/>.</para>
/// </summary>
public class FFmpegException : Exception
{
    /// <summary>
    /// Initializes <see cref="FFmpegException"/>.
    /// </summary>
    public FFmpegException()
    {
    }

    /// <summary>
    /// Initializes <see cref="FFmpegException"/> by specifying exception message.
    /// </summary>
    /// <param name="message">A <c>string</c> represents exception message.</param>
    public FFmpegException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes <see cref="FFmpegException"/> by specifying error or status code.
    /// </summary>
    /// <param name="code">FFmpeg error or status code.</param>
    public FFmpegException(int code) : base(code.FFErrorToText())
    {
    }
}
