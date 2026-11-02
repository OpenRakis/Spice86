namespace Spice86.Shared.Emulator.Errors;

using Spice86.Shared.Emulator.Memory;

using System;

/// <summary>
/// Thrown when generated code returns to an address that a pending outer call expects; the matching call
/// catches it and resumes execution there.
/// </summary>
public class CallStackUnwindException : UnrecoverableException {
    /// <summary>
    /// Initializes a new instance of the CallStackUnwindException class.
    /// </summary>
    /// <param name="target">The address the unwound code jumped to.</param>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public CallStackUnwindException(SegmentedAddress target, string message) : base(message) {
        Target = target;
    }

    /// <summary>
    /// The address the unwound code jumped to.
    /// </summary>
    public SegmentedAddress Target { get; }
}
