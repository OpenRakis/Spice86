namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// Bit mask naming the registers an instruction may write: bits 0-7 are the eight 16-bit general
/// registers in <see cref="Spice86.Core.Emulator.CPU.Registers.RegisterIndex"/> order, bits 8-13
/// the six segment registers in
/// <see cref="Spice86.Core.Emulator.CPU.Registers.SegmentRegisterIndex"/> order.
/// </summary>
[Flags]
internal enum RegisterBits : ushort {
    Ax = 1 << 0,
    Cx = 1 << 1,
    Dx = 1 << 2,
    Bx = 1 << 3,
    Sp = 1 << 4,
    Bp = 1 << 5,
    Si = 1 << 6,
    Di = 1 << 7,
    Es = 1 << 8,
    Cs = 1 << 9,
    Ss = 1 << 10,
    Ds = 1 << 11,
    Fs = 1 << 12,
    Gs = 1 << 13,

    AllGeneralPurpose = Ax | Cx | Dx | Bx | Sp | Bp | Si | Di,
    AllSegment = Es | Cs | Ss | Ds | Fs | Gs,
    All = AllGeneralPurpose | AllSegment,
    AllExceptCs = All & ~Cs
}

/// <summary>
/// Conservative over-approximation of the registers an instruction may write, computed by
/// walking its execution AST. An assignment to a <see cref="RegisterNode"/> (any width) marks the
/// whole 16-bit register; an assignment to a <see cref="SegmentRegisterNode"/> marks the segment
/// register; assignments to memory, temporaries and flags mark nothing. The assigned value is
/// walked too: a <see cref="StackCallValueNode"/> marks SP, pure reads mark nothing, and any
/// value the walker does not understand marks every register except CS. Anything else the walker
/// does not understand - helper calls, string ops, stack pushes, mul/div, control flow other
/// than a plain jump - marks every register except CS, so callers can treat the result as
/// "definitely not written" rather than "exactly written".
/// </summary>
internal static class RegisterWriteSet {
    /// <summary>Returns the <see cref="RegisterBits"/> mask of registers
    /// <paramref name="instruction"/> may write.</summary>
    public static ushort MayWrite(CfgInstruction instruction) => Walk(instruction.ExecutionAst);

    private static ushort Walk(IVisitableAstNode node) => node switch {
        BlockNode block => block.Statements.Aggregate<IVisitableAstNode, ushort>(0, (mask, statement) => (ushort)(mask | Walk(statement))),
        IfElseNode ifElse => (ushort)(Walk(ifElse.TrueCase) | Walk(ifElse.FalseCase)),
        WhileNode whileNode => Walk(whileNode.Body),
        WhileWithExitNode whileWithExit => Walk(whileWithExit.Body),
        BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN, Left: var target, Right: var value } =>
            (ushort)(MaskForTarget(target) | MaskForValue(value)),
        VariableDeclarationNode declaration => MaskForValue(declaration.Initializer),
        AluCallNode or AluCallValueNode => 0,
        JumpNearNode or JumpFarNode or MoveIpNextNode or SelectorNode => 0,
        _ => (ushort)RegisterBits.AllExceptCs
    };

    private static ushort MaskForTarget(IVisitableAstNode target) => target switch {
        SegmentRegisterNode segment => (ushort)(1 << (8 + segment.RegisterIndex)),
        RegisterNode register => (ushort)(1 << GprIndex(register)),
        AbsolutePointerNode or SegmentedPointerNode or VariableDeclarationNode or VariableReferenceNode
            or FlagRegisterNode or CpuFlagNode => 0,
        _ => (ushort)RegisterBits.AllExceptCs
    };

    private static ushort MaskForValue(IVisitableAstNode value) => value switch {
        StackCallValueNode stackCall => (ushort)((ushort)RegisterBits.Sp | MaskForArguments(stackCall)),
        AluCallValueNode aluCall => MaskForArguments(aluCall),
        AbsolutePointerNode absolute => MaskForValue(absolute.AbsoluteAddress),
        SegmentedPointerNode segmented => (ushort)(MaskForValue(segmented.Segment)
            | (segmented.DefaultSegment is null ? 0 : MaskForValue(segmented.DefaultSegment))
            | MaskForValue(segmented.Offset)),
        ConditionalValueNode conditional => (ushort)(MaskForValue(conditional.Condition)
            | MaskForValue(conditional.WhenTrue) | MaskForValue(conditional.WhenFalse)),
        TypeConversionNode conversion => MaskForValue(conversion.Value),
        UnaryOperationNode unary => MaskForValue(unary.Value),
        BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN } assign =>
            (ushort)(MaskForTarget(assign.Left) | MaskForValue(assign.Right)),
        BinaryOperationNode binary => (ushort)(MaskForValue(binary.Left) | MaskForValue(binary.Right)),
        RegisterNode or ConstantNode or InstructionFieldNode or VariableReferenceNode
            or CpuFlagNode or FlagRegisterNode => 0,
        _ => (ushort)RegisterBits.AllExceptCs
    };

    private static ushort MaskForArguments(MethodCallValueNode call) =>
        call.CallNode.Arguments.Aggregate<IVisitableAstNode, ushort>(0,
            (mask, argument) => (ushort)(mask | MaskForValue(argument)));

    /// <summary>The 16-bit register a register node belongs to. 16/32-bit nodes index the eight
    /// GPRs directly; 8-bit nodes use the hardware encoding (0-3 low bytes of AX/CX/DX/BX, 4-7
    /// their high bytes), so a high-byte write maps back to its parent register.</summary>
    private static int GprIndex(RegisterNode register) =>
        register.DataType.BitWidth is BitWidth.BYTE_8 && register.RegisterIndex > 3
            ? register.RegisterIndex - 4
            : register.RegisterIndex;
}
