namespace Spice86.Core.Emulator.Devices.Timer;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Errors;

using System;

/// <summary>
/// The exception thrown when an invalid counter index is used in the Programmable Interval Timer.
/// </summary>
[Serializable]
public class InvalidCounterIndexException : InvalidVMOperationException {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="state">The CPU state.</param>
    /// <param name="counterIndex">The incorrect counter index that triggered the exception.</param>
    public InvalidCounterIndexException(State state, int counterIndex) : base(state, $"Invalid counter index {counterIndex}") {
    }
}