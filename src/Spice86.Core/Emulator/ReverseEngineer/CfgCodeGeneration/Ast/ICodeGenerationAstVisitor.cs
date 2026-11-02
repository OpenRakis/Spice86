namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>Visits the code-generation-only nodes in addition to the runtime AST nodes.</summary>
internal interface ICodeGenerationAstVisitor<T> : IAstVisitor<T> {
    T VisitGotoNode(GotoNode node);
    T VisitLabelNode(LabelNode node);
    T VisitEntryDispatcherLabelNode(EntryDispatcherLabelNode node);
    T VisitCyclicJumpNode(CyclicJumpNode node);
    T VisitReturnNode(ReturnNode node);
    T VisitSwitchNode(SwitchNode node);
    T VisitBreakNode(BreakNode node);
    T VisitCpuFaultTryCatchNode(CpuFaultTryCatchNode node);
    T VisitFailAsUntestedNode(FailAsUntestedNode node);
    T VisitCommentNode(CommentNode node);
    T VisitCheckExternalEventsNode(CheckExternalEventsNode node);
    T VisitSpeculativeGuardNode(SpeculativeGuardNode node);
    T VisitInstructionBoundaryNode(InstructionBoundaryNode node);
    T VisitDivisionNode(DivisionNode node);
    T VisitSegmentVariableNode(SegmentVariableNode node);
    T VisitOffsetLiteralNode(OffsetLiteralNode node);
    T VisitInstructionPointerNode(InstructionPointerNode node);
    T VisitLoadOffsetParameterNode(LoadOffsetParameterNode node);
    T VisitMethodGroupNode(MethodGroupNode node);
    T VisitSegmentedAddressListNode(SegmentedAddressListNode node);
    T VisitSignatureFieldNode(SignatureFieldNode node);
    T VisitExplicitCastNode(ExplicitCastNode node);
    T VisitTupleSwapNode(TupleSwapNode node);
    T VisitContinueNode(ContinueNode node);
    T VisitInfiniteLoopNode(InfiniteLoopNode node);
    T VisitDoWhileNode(DoWhileNode node);
    T VisitAluOperationNode(AluOperationNode node);
    T VisitAluStatementNode(AluStatementNode node);
    T VisitOpaqueNode(OpaqueNode node);
    T VisitInstructionStartNode(InstructionStartNode node);
    T VisitInterruptReturnNode(InterruptReturnNode node);
    T VisitPreTestLoopNode(PreTestLoopNode node);
    T VisitLocalDeclarationNode(LocalDeclarationNode node);
}
