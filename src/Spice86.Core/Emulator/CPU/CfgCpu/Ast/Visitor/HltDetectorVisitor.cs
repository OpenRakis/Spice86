namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;

/// <summary>
/// Visitor that detects whether an AST contains a <see cref="HltNode"/>. A <c>hlt</c>
/// instruction ends no control transfer (its <c>InstructionKind</c> is <c>None</c>), so it
/// cannot be told from an ordinary instruction without looking inside its AST.
/// </summary>
internal sealed class HltDetectorVisitor : StatementPredicateVisitor {
    private static readonly HltDetectorVisitor Instance = new();

    /// <summary>
    /// Returns whether the given AST tree contains a <c>hlt</c>.
    /// </summary>
    public static bool ContainsHlt(IVisitableAstNode node) {
        return node.Accept(Instance);
    }

    public override bool VisitBinaryOperationNode(BinaryOperationNode node) => false;

    public override bool VisitHltNode(HltNode node) => true;
}
