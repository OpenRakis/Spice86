namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;

/// <summary>
/// Detects whether an instruction AST runs code outside the current method or leaves it: a call, an interrupt
/// call, a callback, a return or a halt. Can also answer the halt question alone: a <c>hlt</c> instruction ends
/// no control transfer (its <c>InstructionKind</c> is <c>None</c>), so it cannot be told from an ordinary
/// instruction without looking inside its AST.
/// </summary>
internal sealed class CallReturnHaltDetectorVisitor : StatementPredicateVisitor {
    private static readonly CallReturnHaltDetectorVisitor CallReturnHaltInstance = new(true);
    private static readonly CallReturnHaltDetectorVisitor HltInstance = new(false);

    private readonly bool _callsAndReturns;

    private CallReturnHaltDetectorVisitor(bool callsAndReturns) {
        _callsAndReturns = callsAndReturns;
    }

    /// <summary>Whether <paramref name="node"/> contains a call, interrupt call, callback, return or halt.</summary>
    public static bool ContainsCallReturnOrHalt(IVisitableAstNode node) => node.Accept(CallReturnHaltInstance);

    /// <summary>Whether <paramref name="node"/> contains a <c>hlt</c>.</summary>
    public static bool ContainsHlt(IVisitableAstNode node) => node.Accept(HltInstance);

    public override bool VisitBinaryOperationNode(BinaryOperationNode node) => false;
    public override bool VisitCallNearNode(CallNearNode node) => _callsAndReturns;
    public override bool VisitCallFarNode(CallFarNode node) => _callsAndReturns;
    public override bool VisitInterruptCallNode(InterruptCallNode node) => _callsAndReturns;
    public override bool VisitCallbackNode(CallbackNode node) => _callsAndReturns;
    public override bool VisitReturnNearNode(ReturnNearNode node) => _callsAndReturns;
    public override bool VisitReturnFarNode(ReturnFarNode node) => _callsAndReturns;
    public override bool VisitReturnInterruptNode(ReturnInterruptNode node) => _callsAndReturns;
    public override bool VisitHltNode(HltNode node) => true;
}
