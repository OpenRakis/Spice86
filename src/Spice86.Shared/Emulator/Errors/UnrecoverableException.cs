namespace Spice86.Shared.Emulator.Errors;

using System;

/// <summary>
/// Exception thrown when an unrecoverable error occurs and the emulator cannot continue normal operation.
/// </summary>
[Serializable]
public class UnrecoverableException : Exception {
    /// <summary>
    /// Initializes a new instance of the UnrecoverableException class.
    /// </summary>
    public UnrecoverableException() {
    }

    /// <summary>
    /// Initializes a new instance of the UnrecoverableException class with a specified error message.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public UnrecoverableException(string message) : base(message) {
    }

    /// <summary>
    /// Initializes a new instance of the UnrecoverableException class with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="inner">The exception that is the cause of the current exception, or a null reference if no inner exception is specified.</param>
    public UnrecoverableException(string message, Exception inner) : base(message, inner) {
    }

    /// <summary>
    /// Initializes a new instance of the UnrecoverableException class with serialized data.
    /// </summary>
    /// <param name="info">The SerializationInfo that holds the serialized object data about the exception being thrown.</param>
    /// <param name="context">The StreamingContext that contains contextual information about the source or destination.</param>
    protected UnrecoverableException(
      System.Runtime.Serialization.SerializationInfo info,
      System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}