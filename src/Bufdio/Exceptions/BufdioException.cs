using System;

namespace Bufdio.Exceptions;

/// <summary>
/// An exception that is thrown when an error occured during Bufdio-specific operations.
/// <para>Implements: <see cref="Exception"/>.</para>
/// </summary>
public class BufdioException : Exception
{
    /// <summary>
    /// Initializes <see cref="BufdioException"/>.
    /// </summary>
    public BufdioException()
    {
    }

    /// <summary>
    /// Initializes <see cref="BufdioException"/> by specifying exception message.
    /// </summary>
    /// <param name="message">A <c>string</c> represents exception message.</param>
    public BufdioException(string message) : base(message)
    {
    }
}
