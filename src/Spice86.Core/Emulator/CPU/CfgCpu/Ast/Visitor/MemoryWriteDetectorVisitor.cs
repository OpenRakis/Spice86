namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>
/// Visitor that detects whether an AST contains a memory write (an assignment to
/// an <see cref="AbsolutePointerNode"/> or <see cref="SegmentedPointerNode"/>).
/// </summary>
internal sealed class MemoryWriteDetectorVisitor : StatementPredicateVisitor {
    private static readonly MemoryWriteDetectorVisitor Instance = new();

    /// <summary>
    /// Returns whether the given AST tree contains a memory write.
    /// </summary>
    public static bool ContainsMemoryWrite(IVisitableAstNode node) {
        return node.Accept(Instance);
    }

    public override bool VisitBinaryOperationNode(BinaryOperationNode node) {
        return node.BinaryOperation is BinaryOperation.ASSIGN
               && node.Left is AbsolutePointerNode or SegmentedPointerNode;
    }
}
