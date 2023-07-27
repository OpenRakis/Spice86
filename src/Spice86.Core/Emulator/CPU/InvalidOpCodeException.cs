namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Utils;

using System;

/// <summary>
/// The exception thrown when an Invalid Operation Code was encountered.
/// </summary>
[Serializable]
public class InvalidOpCodeException : InvalidVMOperationException {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="state">The CPU State</param>
    /// <param name="opcode">The OpCode that triggered the exception.</param>
    /// <param name="prefixNotAllowed">Whether an instruction prefix was allowed.</param>
    public InvalidOpCodeException(State state, ushort opcode, bool prefixNotAllowed) : base(state, GenerateMessage(opcode, prefixNotAllowed)) {
    }

    /// <inheritdoc />
    public InvalidOpCodeException(State state, string message) : base(state, message) {
    }

    /// <inheritdoc />
    public InvalidOpCodeException(State state, Exception e) : base(state, e) {
    }

    private static string GenerateMessage(ushort opcode, bool prefixNotAllowed) {
        return $"opcode={ConvertUtils.ToHex(opcode)}{(prefixNotAllowed ? " prefix is not allowed here" : "")}";
    }
}