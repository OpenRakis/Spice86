namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;

/// <summary>
/// Visitor that detects whether an AST starts the interrupt shadow: it contains a
/// <see cref="SetInterruptShadowNode"/>. Combined with <see cref="InterruptEnableDetectorVisitor"/>
/// this identifies <c>sti</c>, the only instruction that both enables interrupts and shadows
/// the next one (<c>mov ss</c> and <c>lss</c> shadow without enabling).
/// </summary>
internal sealed class InterruptShadowDetectorVisitor : StatementPredicateVisitor {
    private static readonly InterruptShadowDetectorVisitor Instance = new();

    /// <summary>
    /// Returns whether the given AST tree starts the interrupt shadow.
    /// </summary>
    public static bool StartsInterruptShadow(IVisitableAstNode node) {
        return node.Accept(Instance);
    }

    public override bool VisitBinaryOperationNode(BinaryOperationNode node) => false;

    public override bool VisitSetInterruptShadowNode(SetInterruptShadowNode node) => true;
}
