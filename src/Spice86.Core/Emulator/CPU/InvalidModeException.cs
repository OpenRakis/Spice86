namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.Errors;
using Spice86.Shared.Utils;

/// <summary>
/// Exception thrown when an invalid mode is encountered during CPU operation.
/// This can occur when the CPU attempts to execute an instruction which triggers a <see cref="ModRM.Read"/> with either an invalid memory offet or an invalid segment register.
/// </summary>
public class InvalidModeException : InvalidVMOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidModeException"/> class.
    /// </summary>
    /// <param name="state">The current state of the CPU.</param>
    /// <param name="mode">The invalid mode that caused the exception.</param>
    public InvalidModeException(State state, uint mode)
        : base(state, $"Invalid mode {ConvertUtils.ToHex((uint)mode)}")
    {
    }
}
