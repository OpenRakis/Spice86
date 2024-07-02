namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.Errors;
using Spice86.Shared.Utils;

/// <summary>
/// The exception thrown when an Invalid Group Index was encountered.
/// </summary>
public class InvalidGroupIndexException : InvalidVMOperationException {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="state">The class that contains the CPU Registers and Flags</param>
    /// <param name="groupIndex">The invalid group index to put in the message, converted to hexadecimal</param>
    public InvalidGroupIndexException(State state, int groupIndex) : base(state, $"Invalid group index {ConvertUtils.ToHex((uint)groupIndex)}") {
    }
    public InvalidGroupIndexException(State state, uint groupIndex) : this(state, (int)groupIndex) {
    }
}