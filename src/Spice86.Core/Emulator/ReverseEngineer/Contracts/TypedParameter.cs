namespace Spice86.Core.Emulator.ReverseEngineer.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;


/// <summary>One parameter or output of a typed signature: its C# name and type, the register or flag
/// expression it stands for at call sites and in the <c>_Asm</c> wrapper, and the
/// <see cref="CpuValues"/> bytes it covers.</summary>
public sealed record TypedParameter(string Name, string Type, string Register, CpuValues Mask) {
    /// <summary>Whether the name carries meaning (a sink or role hint, or a result name) rather
    /// than a generic width name or a register-derived name.</summary>
    public bool HasSemanticName { get; init; }
}
