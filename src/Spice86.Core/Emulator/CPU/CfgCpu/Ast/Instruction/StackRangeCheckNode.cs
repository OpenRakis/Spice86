namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>
/// A segment-limit check of <see cref="Count"/> consecutive stack slots, used only inside a
/// <c>StackExpansion</c>: it is the plain-node form of the access validation the <c>Stack</c> helpers
/// perform, never printed and never a statement on its own. <see cref="Direction.Push"/> checks the
/// slots below <see cref="Offset"/> (offset - size .. offset - count * size),
/// <see cref="Direction.Pop"/> the slots at and above it (offset .. offset + (count - 1) * size).
/// A failing slot throws the stack-segment fault at this point.
/// </summary>
/// <param name="Direction">Whether the checked slots descend or ascend from <see cref="Offset"/>.</param>
/// <param name="Offset">The stack offset the check starts from.</param>
/// <param name="SizeBytes">The size of one checked slot in bytes.</param>
/// <param name="Count">The number of consecutive slots to check.</param>
public sealed record StackRangeCheckNode(StackRangeDirection Direction, ValueNode Offset, int SizeBytes,
    int Count) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => astVisitor.VisitStackRangeCheckNode(this);
}

/// <summary>The direction a <see cref="StackRangeCheckNode"/> walks the stack slots.</summary>
public enum StackRangeDirection {
    /// <summary>Push slots: offset - size, offset - 2 * size, ... descending.</summary>
    Push,
    /// <summary>Pop slots: offset, offset + size, ... ascending.</summary>
    Pop
}
