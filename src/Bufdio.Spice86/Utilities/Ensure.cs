namespace Bufdio.Spice86.Utilities;

using System;
using System.Diagnostics;

/// <summary>
/// Provides methods for ensuring conditions are met, throwing exceptions if they are not.
/// </summary>
[DebuggerStepThrough]
internal static class Ensure {
    /// <summary>
    /// Ensures that the specified condition is true, throwing an exception of the specified type if it is not.
    /// </summary>
    /// <typeparam name="TException">The type of exception to throw if the condition is false.</typeparam>
    /// <param name="condition">The condition to check.</param>
    /// <param name="message">An optional error message to include in the exception.</param>
    /// <exception>Throws an exception of type <typeparamref name="TException"/> when the condition is false.</exception>
    public static void That<TException>(bool condition, string? message = null) where TException : Exception {
        if (!condition) {
            throw string.IsNullOrWhiteSpace(message)
                ? Activator.CreateInstance<TException>()
                : (TException)Activator.CreateInstance(typeof(TException), message)!;
        }
    }
}