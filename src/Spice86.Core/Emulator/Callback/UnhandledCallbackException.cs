namespace Spice86.Core.Emulator.Callback;

using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.VM;

using System;

/// <summary>
/// Exception signaling that the callback number that was meant to be executed was not mapped to any
/// csharp code. <br /> Could happen for unhandled exceptions.
/// </summary>
[Serializable]
public class UnhandledCallbackException : UnhandledOperationException {
    /// <summary>
    /// Initializes a new instance of the <see cref="UnhandledCallbackException"/> class with a specified machine and callback number.
    /// </summary>
    /// <param name="machine">The <see cref="Machine"/> instance that encountered the unhandled callback exception.</param>
    /// <param name="callbackNumber">Indicates which callback we attempted to call.</param>
    /// 
    public UnhandledCallbackException(Machine machine, int callbackNumber) : base(machine, FormatMessage(callbackNumber)) {
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