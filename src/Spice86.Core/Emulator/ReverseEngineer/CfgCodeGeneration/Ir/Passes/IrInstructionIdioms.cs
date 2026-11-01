namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// Rewrites the multi-statement shapes three instruction idioms lower to, on the flat statements of
/// an <see cref="IrMethod"/>. Only the statements lifted from one instruction can form a shape, so a
/// candidate run must sit in one block and share the origin instruction whose
/// <see cref="IrMethod.Shapes"/> entry is <see cref="IrInstructionShape.Exchange"/> or
/// <see cref="IrInstructionShape.LoadFarPointer"/>, or be untagged. An <c>xchg</c> of two registers
/// whose bytes do not overlap becomes a single <see cref="TupleSwapNode"/>; a <c>les</c> / <c>lds</c>
/// quadruple, or the triple <see cref="IrPlainTemporaryPropagation"/> leaves of it, becomes the two
/// direct assignments, refused when the segment read's address reads a
/// byte of the value register; a <c>mul</c> / <c>imul</c> result-declaration triple becomes
/// <c>AX = call</c> for 8 bits or a <see cref="MultiplicationNode"/> for 16 and 32 bits; a
/// segment-register <c>pop</c> pair becomes the same
/// <see cref="StackCallValueNode"/> assignment a general register pop uses. Runs after
/// <see cref="IrPlainTemporaryPropagation"/>.
/// </summary>
internal static class IrInstructionIdioms {
    /// <summary>Rewrites the recognized idioms inside every block of <paramref name="method"/>.</summary>
    public static void Apply(IrMethod method) {
        foreach (List<IrStatement> statements in method.Blocks.Select(block => block.Statements)) {
            for (int i = 0; i < statements.Count; i++) {
                if (ShapeOf(method, statements, i, 3) == IrInstructionShape.Exchange
                    && TupleSwap(statements, i) is { } swap) {
                    statements[i] = IrStatement.Create(swap, statements[i].Origin);
                    statements.RemoveRange(i + 1, 2);
                } else if (ShapeOf(method, statements, i, 4) == IrInstructionShape.LoadFarPointer
                    && LoadFarPointerLifted(statements, i) is { } lifted) {
                    statements[i] = IrStatement.Create(lifted.Value, statements[i].Origin);
                    statements[i + 1] = IrStatement.Create(lifted.Segment, statements[i + 1].Origin);
                    statements.RemoveRange(i + 2, 2);
                    i++;
                } else if (ShapeOf(method, statements, i, 3) == IrInstructionShape.LoadFarPointer
                    && LoadFarPointerPropagated(statements, i) is { } propagated) {
                    statements[i] = IrStatement.Create(propagated.Value, statements[i].Origin);
                    statements[i + 1] = IrStatement.Create(propagated.Segment, statements[i + 1].Origin);
                    statements.RemoveRange(i + 2, 1);
                    i++;
                } else if (Multiplication(statements, i) is { } multiplication) {
                    statements[i] = IrStatement.Create(multiplication, statements[i].Origin);
                    statements.RemoveRange(i + 1, 2);
                } else if (ShapeOf(method, statements, i, 2) == IrInstructionShape.None
                    && SegmentPop(statements, i) is { } pop) {
                    statements[i] = IrStatement.Create(pop, statements[i].Origin);
                    statements.RemoveRange(i + 1, 1);
                }
            }
        }
    }

    /// <summary>
    /// The instruction shape of a run of <paramref name="count"/> consecutive statements when they all
    /// come from one instruction, or <see cref="IrInstructionShape.None"/>.
    /// </summary>
    private static IrInstructionShape ShapeOf(IrMethod method, IReadOnlyList<IrStatement> statements,
        int index, int count) {
        if (index + count > statements.Count) {
            return IrInstructionShape.None;
        }
        ICfgNode origin = statements[index].Origin;
        for (int i = index + 1; i < index + count; i++) {
            if (!statements[i].Origin.Equals(origin)) {
                return IrInstructionShape.None;
            }
        }
        return method.Shapes.GetValueOrDefault(origin, IrInstructionShape.None);
    }

    /// <summary>The <c>xchg</c> shape <c>temp = r1; r1 = r2; r2 = temp</c> of two general registers of the
    /// same type whose bytes do not overlap.</summary>
    private static TupleSwapNode? TupleSwap(IReadOnlyList<IrStatement> statements, int index) {
        if (statements[index].Node is not VariableDeclarationNode
                { Initializer: RegisterNode first } declaration
            || first is SegmentRegisterNode
            || statements[index + 1].Node is not BinaryOperationNode
                { BinaryOperation: BinaryOperation.ASSIGN, Right: RegisterNode second } firstAssign
            || second is SegmentRegisterNode
            || !firstAssign.Left.Equals(first)
            || first.DataType != second.DataType
            || statements[index + 2].Node is not BinaryOperationNode
                { BinaryOperation: BinaryOperation.ASSIGN, Right: VariableReferenceNode reference } secondAssign
            || reference.VariableName != declaration.VariableName
            || !secondAssign.Left.Equals(second)
            || (IrValueSets.Of(first) & IrValueSets.Of(second)) != CpuValues.None) {
            return null;
        }
        return new TupleSwapNode(first, second);
    }

    /// <summary>The <c>les</c> / <c>lds</c> shape <c>V = m1; S = m2; R = V; Seg = S</c>: two temporaries each
    /// read once by the copy right after.</summary>
    private static (BinaryOperationNode Value, BinaryOperationNode Segment)? LoadFarPointerLifted(
        IReadOnlyList<IrStatement> statements, int index) {
        if (statements[index].Node is not VariableDeclarationNode
                { Initializer: SegmentedPointerNode or AbsolutePointerNode } valueDeclaration
            || statements[index + 1].Node is not VariableDeclarationNode
                { Initializer: SegmentedPointerNode or AbsolutePointerNode } segmentDeclaration
            || statements[index + 2].Node is not BinaryOperationNode
                { BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode register,
                    Right: VariableReferenceNode valueReference }
            || valueReference.VariableName != valueDeclaration.VariableName
            || statements[index + 3].Node is not BinaryOperationNode
                { BinaryOperation: BinaryOperation.ASSIGN, Left: SegmentRegisterNode,
                    Right: VariableReferenceNode segmentReference } copySegment
            || segmentReference.VariableName != segmentDeclaration.VariableName
            || register is SegmentRegisterNode) {
            return null;
        }
        return LoadFarPointer(valueDeclaration, register, segmentDeclaration.Initializer, copySegment);
    }

    /// <summary>The same shape after <see cref="IrPlainTemporaryPropagation"/> inlined the segment
    /// temporary: <c>V = m1; R = V; Seg = m2</c>.</summary>
    private static (BinaryOperationNode Value, BinaryOperationNode Segment)? LoadFarPointerPropagated(
        IReadOnlyList<IrStatement> statements, int index) {
        if (statements[index].Node is not VariableDeclarationNode
                { Initializer: SegmentedPointerNode or AbsolutePointerNode } valueDeclaration
            || statements[index + 1].Node is not BinaryOperationNode
                { BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode register,
                    Right: VariableReferenceNode valueReference }
            || valueReference.VariableName != valueDeclaration.VariableName
            || statements[index + 2].Node is not BinaryOperationNode
                { BinaryOperation: BinaryOperation.ASSIGN, Left: SegmentRegisterNode,
                    Right: SegmentedPointerNode or AbsolutePointerNode } copySegment
            || register is SegmentRegisterNode) {
            return null;
        }
        return LoadFarPointer(valueDeclaration, register, copySegment.Right, copySegment);
    }

    /// <summary>The rewrite both <c>les</c> / <c>lds</c> shapes share: it assigns <c>R = m1</c> before
    /// <c>Seg = m2</c>, so it is refused when the address <c>m2</c> reads a byte of <c>R</c>.</summary>
    private static (BinaryOperationNode Value, BinaryOperationNode Segment)? LoadFarPointer(
        VariableDeclarationNode valueDeclaration, RegisterNode register, ValueNode segmentRead,
        BinaryOperationNode copySegment) {
        if ((IrEffectsVisitor.Of(segmentRead, false).Reads
             & IrValueSets.Of(register)) != CpuValues.None) {
            return null;
        }
        return (new BinaryOperationNode(register.DataType, register, BinaryOperation.ASSIGN,
                valueDeclaration.Initializer),
            copySegment with { Right = segmentRead });
    }

    /// <summary>Whether three consecutive statements share one origin instruction whose display
    /// operation is MUL or IMUL.</summary>
    private static bool MulImulOrigin(IReadOnlyList<IrStatement> statements, int index) {
        if (index + 3 > statements.Count
            || statements[index].Origin is not CfgInstruction
                { DisplayAst.Operation: InstructionOperation.MUL or InstructionOperation.IMUL }
                instruction) {
            return false;
        }
        return statements[index + 1].Origin.Equals(instruction)
            && statements[index + 2].Origin.Equals(instruction);
    }

    /// <summary>The <c>mul</c> / <c>imul</c> shape <c>result = AluN.Mul|Imul(a, b);
    /// high = (T)(result &gt;&gt; width); low = (T)result</c>, with the result variable read by those
    /// two statements only. The 8-bit form becomes <c>AX = call</c> (AX is the wide register of the
    /// low target); the 16- and 32-bit forms become a <see cref="MultiplicationNode"/>.</summary>
    private static IVisitableAstNode? Multiplication(IReadOnlyList<IrStatement> statements, int index) {
        if (index + 3 > statements.Count
            || statements[index].Node is not VariableDeclarationNode
                { Initializer: AluOperationNode
                    { Call: { Operation: AluOperation.Mul or AluOperation.Imul } call } operation } declaration
            || statements[index + 1].Node is not BinaryOperationNode
                { BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode high,
                    Right: TypeConversionNode
                        { Value: BinaryOperationNode
                            { BinaryOperation: BinaryOperation.RIGHT_SHIFT,
                                Left: VariableReferenceNode highSource,
                                Right: ConstantNode highShift } } }
            || highSource.VariableName != declaration.VariableName
            || highShift.Value != (ulong)(int)call.Width
            || statements[index + 2].Node is not BinaryOperationNode
                { BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode low,
                    Right: TypeConversionNode { Value: VariableReferenceNode lowSource } }
            || lowSource.VariableName != declaration.VariableName
            || ResultReadElsewhere(statements, index, declaration.VariableName)
            || !MulImulOrigin(statements, index)) {
            return null;
        }
        if (call.Width == BitWidth.BYTE_8) {
            if (low.DataType != DataType.UINT8 || high.DataType != DataType.UINT8
                || high.RegisterIndex != low.RegisterIndex + 4) {
                return null;
            }
            RegisterNode wide = new(DataType.UINT16, low.RegisterIndex);
            ValueNode value = call.DataType == DataType.UINT16 ? operation
                : new TypeConversionNode(DataType.UINT16, operation);
            return new BinaryOperationNode(DataType.UINT16, wide, BinaryOperation.ASSIGN, value);
        }
        if (call.Width is not (BitWidth.WORD_16 or BitWidth.DWORD_32)
            || low.RegisterIndex != (int)RegisterIndex.AxIndex
            || high.RegisterIndex != (int)RegisterIndex.DxIndex) {
            return null;
        }
        return new MultiplicationNode(call.Width, call.Operation == AluOperation.Imul,
            UnwrapSignedness((ValueNode)call.CallNode.Arguments[0]),
            UnwrapSignedness((ValueNode)call.CallNode.Arguments[1]), low, high);
    }

    /// <summary>Drops a conversion that changes only the signedness of its operand: the printer casts
    /// the argument to the helper's parameter type, so <c>(short)BX</c> and <c>BX</c> hold the same
    /// bits once cast.</summary>
    private static ValueNode UnwrapSignedness(ValueNode node) =>
        node is TypeConversionNode { Value: { } inner }
            && inner.DataType.BitWidth == node.DataType.BitWidth ? inner : node;

    /// <summary>Whether the result variable is read by a statement outside the matched triple; the
    /// declaration lives in one block, so only this statement list is scanned.</summary>
    private static bool ResultReadElsewhere(IReadOnlyList<IrStatement> statements, int index,
        string variableName) {
        for (int i = 0; i < statements.Count; i++) {
            if (i >= index && i <= index + 2) {
                continue;
            }
            if (IrAstChildren.Descendants(statements[i].Node)
                .OfType<VariableReferenceNode>().Any(reference => reference.VariableName == variableName)) {
                return true;
            }
        }
        return false;
    }

    /// <summary>The segment-register <c>pop</c> shape <c>Seg = UINT16 [SS:SP]; SP = SP + 2</c>: the
    /// general register pop is already one <see cref="StackCallValueNode"/> assignment, so the
    /// segment form takes the same call with the segment as its target. <c>pop ss</c> and the
    /// 32-bit-operand form (<c>SP + 4</c>) are not matched.</summary>
    private static BinaryOperationNode? SegmentPop(IReadOnlyList<IrStatement> statements, int index) {
        if (statements[index].Node is not BinaryOperationNode
                { BinaryOperation: BinaryOperation.ASSIGN, Left: SegmentRegisterNode segment,
                    Right: SegmentedPointerNode
                        { Segment: SegmentRegisterNode pointerSegment, DefaultSegment: null,
                            Offset: RegisterNode stackPointer } pointer }
            || segment.RegisterIndex == (int)SegmentRegisterIndex.SsIndex
            || pointer.DataType != DataType.UINT16
            || pointerSegment.RegisterIndex != (int)SegmentRegisterIndex.SsIndex
            || stackPointer.DataType != DataType.UINT16
            || stackPointer.RegisterIndex != (int)RegisterIndex.SpIndex) {
            return null;
        }
        if (statements[index + 1].Node is not BinaryOperationNode
                { BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode advanceTarget,
                    Right: BinaryOperationNode
                        { BinaryOperation: BinaryOperation.PLUS, Left: RegisterNode advanceSource,
                            Right: ConstantNode { Value: 2 } } }
            || !advanceTarget.Equals(stackPointer)
            || !advanceSource.Equals(stackPointer)) {
            return null;
        }
        return new BinaryOperationNode(segment.DataType, segment, BinaryOperation.ASSIGN,
            new StackCallValueNode(StackOperation.Pop, BitWidth.WORD_16));
    }
}
