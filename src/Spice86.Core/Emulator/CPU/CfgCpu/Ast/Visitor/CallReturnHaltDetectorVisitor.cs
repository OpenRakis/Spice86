namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;

/// <summary>
/// Detects whether an instruction AST runs code outside the current method or leaves it: a call, an interrupt
/// call, a callback, a return or a halt.
/// </summary>
internal sealed class CallReturnHaltDetectorVisitor : StatementPredicateVisitor {
    private static readonly CallReturnHaltDetectorVisitor Instance = new();

    /// <summary>Whether <paramref name="node"/> contains a call, interrupt call, callback, return or halt.</summary>
    public static bool ContainsCallReturnOrHalt(IVisitableAstNode node) => node.Accept(Instance);

    public override bool VisitBinaryOperationNode(BinaryOperationNode node) => false;
    public override bool VisitCallNearNode(CallNearNode node) => true;
    public override bool VisitCallFarNode(CallFarNode node) => true;
    public override bool VisitInterruptCallNode(InterruptCallNode node) => true;
    public override bool VisitCallbackNode(CallbackNode node) => true;
    public override bool VisitReturnNearNode(ReturnNearNode node) => true;
    public override bool VisitReturnFarNode(ReturnFarNode node) => true;
    public override bool VisitReturnInterruptNode(ReturnInterruptNode node) => true;
    public override bool VisitHltNode(HltNode node) => true;
}
