namespace Spice86.Core.Emulator.InterruptHandlers.Common.Callback;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Errors;

/// <summary>
/// Exception signaling that the callback number that was meant to be executed was not mapped to any
/// csharp code. <br /> Could happen for unhandled exceptions.
/// </summary>
[Serializable]
public class UnhandledCallbackException : UnhandledOperationException {
    /// <summary>
    /// Initializes a new instance of the <see cref="UnhandledCallbackException"/> class with a specified machine and callback number.
    /// </summary>
    /// <param name="state">The CPU state.</param>
    /// <param name="callbackNumber">Indicates which callback we attempted to call.</param>
    public UnhandledCallbackException(ICpuState state, int callbackNumber) : base(state, FormatMessage(callbackNumber)) {
    }

    /// <summary>
    /// Formats a message string for the unhandled callback exception with the specified callback number.
    /// </summary>
    /// <param name="callbackNumber">The number of the unhandled callback exception.</param>
    /// <returns>The formatted message string.</returns>
    private static string FormatMessage(int callbackNumber) {
        return $"callbackNumber=0x{callbackNumber:x}";
    }
}