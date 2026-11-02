namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using System.Linq;

/// <summary>
/// Rewrites the multi-statement shapes two instruction idioms lower to, on the flat statements of an
/// <see cref="IrMethod"/>. Only the statements lifted from one instruction can form a shape, so a
/// candidate run must sit in one block and share the origin instruction whose
/// <see cref="IrMethod.Shapes"/> entry is <see cref="IrInstructionShape.Exchange"/> or
/// <see cref="IrInstructionShape.LoadFarPointer"/>. An <c>xchg</c> of two registers whose bytes do not
/// overlap becomes a single <see cref="TupleSwapNode"/>; a <c>les</c> / <c>lds</c> quadruple becomes
/// the two direct assignments, refused when the segment read's address reads a byte of the value
/// register. Runs after <see cref="IrPlainTemporaryPropagation"/>.
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
                    && LoadFarPointer(statements, i) is { } farPointer) {
                    statements[i] = IrStatement.Create(farPointer.Value, statements[i].Origin);
                    statements[i + 1] = IrStatement.Create(farPointer.Segment, statements[i + 1].Origin);
                    statements.RemoveRange(i + 2, 2);
                    i++;
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
            || (IrValueSets.Of(first) & IrValueSets.Of(second)) != IrValues.None) {
            return null;
        }
        return new TupleSwapNode(first, second);
    }

    /// <summary>The <c>les</c> / <c>lds</c> shape <c>V = m1; S = m2; R = V; Seg = S</c>: two temporaries each
    /// read once by the copy right after. The rewrite assigns <c>R = m1</c> before <c>Seg = m2</c>, so it is
    /// refused when the address <c>m2</c> reads a byte of <c>R</c>.</summary>
    private static (BinaryOperationNode Value, BinaryOperationNode Segment)? LoadFarPointer(
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
        if ((IrEffectsVisitor.Of(segmentDeclaration.Initializer, false).Reads
             & IrValueSets.Of(register)) != IrValues.None) {
            return null;
        }
        return (new BinaryOperationNode(register.DataType, register, BinaryOperation.ASSIGN,
                valueDeclaration.Initializer),
            copySegment with { Right = segmentDeclaration.Initializer });
    }
}
