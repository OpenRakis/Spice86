namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Thin <see cref="IAstVisitor{T}"/> wrapper that delegates rendering to
/// <see cref="AstFormattedTokenRenderer"/> with a <see cref="StringRenderer"/> and returns
/// the accumulated plain-text string. Preserves the existing string-returning visitor interface
/// for callers such as <see cref="Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph.NodeToString"/>.
/// </summary>
public class AstInstructionRenderer : IAstVisitor<string> {
    private readonly StringRenderer _stringRenderer = new();
    private readonly AstFormattedTokenRenderer _tokenRenderer;

    public AstInstructionRenderer(AsmRenderingConfig config) {
        _tokenRenderer = new AstFormattedTokenRenderer(config, _stringRenderer);
    }

    private string Render(IVisitableAstNode node) {
        _stringRenderer.Reset();
        node.Accept(_tokenRenderer);
        return _stringRenderer.GetResult();
    }

    /// <inheritdoc/>
    public string VisitSegmentRegisterNode(SegmentRegisterNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitSegmentedPointer(SegmentedPointerNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitRegisterNode(RegisterNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitAbsolutePointerNode(AbsolutePointerNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitCpuFlagNode(CpuFlagNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitConstantNode(ConstantNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitNearAddressNode(NearAddressNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitSegmentedAddressConstantNode(SegmentedAddressConstantNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitBinaryOperationNode(BinaryOperationNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitUnaryOperationNode(UnaryOperationNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitTypeConversionNode(TypeConversionNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitInstructionNode(InstructionNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitMethodCallNode(MethodCallNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitMethodCallValueNode(MethodCallValueNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitBlockNode(BlockNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitIfElseNode(IfElseNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitVariableReferenceNode(VariableReferenceNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitVariableDeclarationNode(VariableDeclarationNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitMoveIpNextNode(MoveIpNextNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitCallNearNode(CallNearNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitCallFarNode(CallFarNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitReturnNearNode(ReturnNearNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitReturnFarNode(ReturnFarNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitJumpNearNode(JumpNearNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitJumpFarNode(JumpFarNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitInterruptCallNode(InterruptCallNode node) => Render(node);

    /// <inheritdoc/>
    public string VisitReturnInterruptNode(ReturnInterruptNode node) => Render(node);
}
