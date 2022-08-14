using System;
using Bufdio.Utilities.Extensions;

namespace Bufdio.Exceptions;

/// <summary>
/// An exception that is thrown when errors occured in internal PortAudio processes.
/// <para>Implements: <see cref="Exception"/>.</para>
/// </summary>
public class PortAudioException : Exception
{
    /// <summary>
    /// Initializes <see cref="PortAudioException"/>.
    /// </summary>
    public PortAudioException()
    {
    }

    /// <summary>
    /// Initializes <see cref="PortAudioException"/> by specifying exception message.
    /// </summary>
    /// <param name="message">A <c>string</c> represents exception message.</param>
    public PortAudioException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes <see cref="PortAudioException"/> by specifying error or status code.
    /// </summary>
    /// <param name="code">PortAudio error or status code.</param>
    public PortAudioException(int code) : base(code.PaErrorToText())
    {
    }
}
