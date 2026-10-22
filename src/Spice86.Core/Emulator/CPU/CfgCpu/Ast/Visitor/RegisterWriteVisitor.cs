namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Computes the general-purpose registers an instruction AST may write. A register is reported by its 16-bit
/// name, so writing AL, AH or EAX reports AX. Code the analysis does not know (unknown method calls,
/// interrupts, callbacks, calls, returns, unrecognised instructions) writes every register.
/// </summary>
internal sealed class RegisterWriteVisitor : IAstVisitor<GeneralRegisters> {
    private static readonly RegisterWriteVisitor Instance = new();

    /// <summary>The general-purpose registers <paramref name="node"/> may write.</summary>
    public static GeneralRegisters Of(IVisitableAstNode node) => node.Accept(Instance);

    /// <summary>The general-purpose register that holds <paramref name="register"/>; <see cref="GeneralRegisters.None"/> for a segment register.</summary>
    public static GeneralRegisters Covering(RegisterNode register) {
        if (register is SegmentRegisterNode) {
            return GeneralRegisters.None;
        }
        uint index = (uint)register.RegisterIndex;
        if (register.DataType.BitWidth == BitWidth.BYTE_8) {
            index = UInt8HighLowRegistersIndexer.ComputeRegisterIndexInArray(index);
        }
        return (GeneralRegisters)(1 << (int)index);
    }

    private RegisterWriteVisitor() {
    }

    public GeneralRegisters VisitBlockNode(BlockNode node) {
        GeneralRegisters written = GeneralRegisters.None;
        foreach (IVisitableAstNode statement in node.Statements) {
            written |= statement.Accept(this);
        }
        return written;
    }

    public GeneralRegisters VisitIfElseNode(IfElseNode node) =>
        node.Condition.Accept(this) | node.TrueCase.Accept(this) | node.FalseCase.Accept(this);

    public GeneralRegisters VisitWhileNode(WhileNode node) =>
        node.Condition.Accept(this) | node.Body.Accept(this);

    public GeneralRegisters VisitWhileWithExitNode(WhileWithExitNode node) =>
        node.Condition.Accept(this) | node.Body.Accept(this) | node.ExitCondition.Accept(this);

    public GeneralRegisters VisitBinaryOperationNode(BinaryOperationNode node) {
        if (node.BinaryOperation == BinaryOperation.ASSIGN && node.Left is RegisterNode register) {
            return Covering(register) | node.Right.Accept(this);
        }
        return node.Left.Accept(this) | node.Right.Accept(this);
    }

    public GeneralRegisters VisitUnaryOperationNode(UnaryOperationNode node) => node.Value.Accept(this);

    public GeneralRegisters VisitTypeConversionNode(TypeConversionNode node) => node.Value.Accept(this);

    public GeneralRegisters VisitMethodCallValueNode(MethodCallValueNode node) => node.CallNode.Accept(this);

    public GeneralRegisters VisitMethodCallNode(MethodCallNode node) {
        GeneralRegisters written = GeneralRegisters.None;
        foreach (IVisitableAstNode argument in node.Arguments) {
            written |= argument.Accept(this);
        }
        return written | CallWrites(node);
    }

    public GeneralRegisters VisitVariableDeclarationNode(VariableDeclarationNode node) => node.Initializer.Accept(this);

    public GeneralRegisters VisitInstructionFieldNode(InstructionFieldNode node) => node.ResolvedNode.Accept(this);

    public GeneralRegisters VisitSegmentedPointer(SegmentedPointerNode node) {
        GeneralRegisters written = node.Segment.Accept(this);
        if (node.DefaultSegment != null) {
            written |= node.DefaultSegment.Accept(this);
        }
        return written | node.Offset.Accept(this);
    }

    public GeneralRegisters VisitAbsolutePointerNode(AbsolutePointerNode node) => node.AbsoluteAddress.Accept(this);

    public GeneralRegisters VisitSegmentedAddressNode(SegmentedAddressNode node) =>
        node.Segment.Accept(this) | node.Offset.Accept(this);

    public GeneralRegisters VisitJumpNearNode(JumpNearNode node) => node.Ip.Accept(this);

    public GeneralRegisters VisitJumpFarNode(JumpFarNode node) => node.TargetAddress.Accept(this);

    public GeneralRegisters VisitCallNearNode(CallNearNode node) => GeneralRegisters.All;

    public GeneralRegisters VisitCallFarNode(CallFarNode node) => GeneralRegisters.All;

    public GeneralRegisters VisitReturnNearNode(ReturnNearNode node) => GeneralRegisters.All;

    public GeneralRegisters VisitReturnFarNode(ReturnFarNode node) => GeneralRegisters.All;

    public GeneralRegisters VisitReturnInterruptNode(ReturnInterruptNode node) => GeneralRegisters.All;

    public GeneralRegisters VisitInterruptCallNode(InterruptCallNode node) => GeneralRegisters.All;

    public GeneralRegisters VisitCallbackNode(CallbackNode node) => GeneralRegisters.All;

    public GeneralRegisters VisitInstructionNode(InstructionNode node) => GeneralRegisters.All;

    public GeneralRegisters VisitRegisterNode(RegisterNode node) => GeneralRegisters.None;

    public GeneralRegisters VisitSegmentRegisterNode(SegmentRegisterNode node) => GeneralRegisters.None;

    public GeneralRegisters VisitConstantNode(ConstantNode node) => GeneralRegisters.None;

    public GeneralRegisters VisitNearAddressNode(NearAddressNode node) => GeneralRegisters.None;

    public GeneralRegisters VisitVariableReferenceNode(VariableReferenceNode node) => GeneralRegisters.None;

    public GeneralRegisters VisitCpuFlagNode(CpuFlagNode node) => GeneralRegisters.None;

    public GeneralRegisters VisitFlagRegisterNode(FlagRegisterNode node) => GeneralRegisters.None;

    public GeneralRegisters VisitThrowNode(ThrowNode node) => GeneralRegisters.None;

    public GeneralRegisters VisitMoveIpNextNode(MoveIpNextNode node) => GeneralRegisters.None;

    public GeneralRegisters VisitHltNode(HltNode node) => GeneralRegisters.None;

    public GeneralRegisters VisitSelectorNode(SelectorNode node) => GeneralRegisters.None;

    public GeneralRegisters VisitInvalidInstructionNode(InvalidInstructionNode node) => GeneralRegisters.None;

    public GeneralRegisters VisitCpuidNode(CpuidNode node) => GeneralRegisters.None;

    private static GeneralRegisters CallWrites(MethodCallNode node) {
        string? propertyPath = node.PropertyPath;
        if (propertyPath == "Stack") {
            return StackWrites(node.MethodName);
        }
        if (propertyPath is "Alu8" or "Alu16" or "Alu32" or "State") {
            return GeneralRegisters.None;
        }
        if (propertyPath is null && IsNeutralRootMethod(node.MethodName)) {
            return GeneralRegisters.None;
        }
        return GeneralRegisters.All;
    }

    private static GeneralRegisters StackWrites(string methodName) {
        if (methodName is "Push16" or "Push32" or "Pop16" or "Pop32" or "Peek8" or "Peek16" or "Peek32") {
            return GeneralRegisters.Sp;
        }
        return GeneralRegisters.All;
    }

    private static bool IsNeutralRootMethod(string methodName) =>
        methodName is "In8" or "In16" or "In32" or "Out8" or "Out16" or "Out32"
            or "CheckBound" or "BitScanForward16" or "BitScanForward32" or "BitScanReverse16" or "BitScanReverse32";
}
