namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.Errors;
using Spice86.Shared.Utils;

/// <summary>
/// Exception thrown when an invalid register memory index is encountered during CPU operation.
/// </summary>
public class InvalidRegisterMemoryIndexException : InvalidVMOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidRegisterMemoryIndexException"/> class.
    /// </summary>
    /// <param name="state">The current state of the CPU.</param>
    /// <param name="registerMemoryIndex">The invalid register memory index that caused the exception.</param>
    public InvalidRegisterMemoryIndexException(State state, int registerMemoryIndex)
        : base(state, $"Register memory index must be between 0 and 7 inclusive. Was {ConvertUtils.ToHex((uint)registerMemoryIndex)}")
    {
    }
}
