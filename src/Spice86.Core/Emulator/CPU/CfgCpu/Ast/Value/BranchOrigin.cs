namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// The instruction a branch condition or a switch value of generated code comes from, as the code generator
/// records it in <see cref="ValueNode.BranchOrigin"/>.
/// </summary>
/// <param name="Address">Address of the instruction that decides the branch: a conditional jump, a loop, an
/// indirect jump or call, or an instruction with a condition of its own. <c>null</c> for a synthetic
/// condition, one that no single instruction decides.</param>
internal sealed record BranchOrigin(SegmentedAddress? Address) {
    /// <summary>The origin of a synthetic condition.</summary>
    public static BranchOrigin Synthetic { get; } = new((SegmentedAddress?)null);
}
