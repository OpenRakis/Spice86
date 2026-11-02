namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

/// <summary>
/// An edge from a block terminator to a target block. An edge with <see cref="InsideInstruction"/> stays
/// within the control flow one instruction lowers to; any other edge goes to another instruction.
/// </summary>
/// <param name="target">The block the edge jumps to.</param>
/// <param name="insideInstruction">Whether the edge stays inside one instruction's control flow.</param>
internal sealed class IrEdge(IrBlock target, bool insideInstruction) {
    /// <summary>The block the edge jumps to.</summary>
    public IrBlock Target { get; } = target;

    /// <summary>Whether the edge stays inside one instruction's control flow.</summary>
    public bool InsideInstruction { get; } = insideInstruction;
}
