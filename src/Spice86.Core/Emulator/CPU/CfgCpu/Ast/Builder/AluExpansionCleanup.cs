namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

using System.Linq;

/// <summary>
/// Shrinks an <see cref="AluExpansion"/> to what its context needs: drops the assignments of dead flags and the
/// temps nobody reads, and inlines temps where every read still happens once and in the same order. A result that
/// is an operand xored with or subtracted from itself is zero. A flag read is inlined only when no flag assignment
/// is kept, so no flag is written between the read and its use.
/// </summary>
internal static class AluExpansionCleanup {
    private enum ReadKind {
        Pure,
        Memory,
        Flag,
        Other
    }

    /// <summary>The cleaned form of <paramref name="expansion"/>.</summary>
    /// <param name="expansion">The expansion of one ALU call.</param>
    /// <param name="liveFlags">The status flags that are read after the call before being written again.</param>
    /// <param name="valueUsed">Whether the caller uses the value of the call (false for <c>cmp</c> and <c>test</c>).</param>
    public static CleanedAluExpansion Clean(AluExpansion expansion, StatusFlags liveFlags, bool valueUsed) {
        List<AluFlagAssignment> flags = expansion.FlagAssignments
            .Where(assignment => (assignment.Flag & liveFlags) != StatusFlags.None).ToList();
        VariableDeclarationNode result = expansion.Result;
        int resultUses = flags.Sum(assignment => CountReferences(assignment.Value, result.Reference));
        if (valueUsed) {
            resultUses++;
        }
        int impureOperands = expansion.Operands.Count(operand =>
            Classify(operand.Initializer) is ReadKind.Memory or ReadKind.Other);
        bool flagAssignmentsKept = flags.Count != 0;
        Dictionary<VariableReferenceNode, ValueNode> replacements = new();
        List<IVisitableAstNode> statements = [];
        foreach (VariableDeclarationNode operand in expansion.Operands) {
            int uses = flags.Sum(assignment => CountReferences(assignment.Value, operand.Reference));
            if (resultUses > 0) {
                uses += CountReferences(result.Initializer, operand.Reference);
            }
            ReadKind kind = Classify(operand.Initializer);
            if (CanInlineOperand(operand.Initializer, kind, uses, impureOperands, flagAssignmentsKept)) {
                replacements[operand.Reference] = WithType(operand.Initializer, operand.DataType);
            } else if (uses > 0 || kind is not (ReadKind.Pure or ReadKind.Flag)) {
                statements.Add(operand);
            }
        }
        if (resultUses > 0) {
            ValueNode resultValue = WithoutSelfCancellation(Substitute(result.Initializer, replacements));
            if (resultUses == 1 && Classify(resultValue) != ReadKind.Other) {
                replacements[result.Reference] = resultValue;
            } else {
                statements.Add(new VariableDeclarationNode(result.DataType, result.VariableName, resultValue));
            }
        }
        statements.AddRange(flags.Select(assignment =>
            new AluFlagAssignment(assignment.Flag, Substitute(assignment.Value, replacements)).ToAssignment()));
        ValueNode? value = valueUsed ? Substitute(result.Reference, replacements) : null;
        return new CleanedAluExpansion(statements, value);
    }

    private static bool CanInlineOperand(ValueNode initializer, ReadKind kind, int uses, int impureOperands,
        bool flagAssignmentsKept) {
        if (kind == ReadKind.Flag) {
            return uses == 1 && !flagAssignmentsKept;
        }
        bool isPureOrSingleMemory = kind == ReadKind.Pure || (kind == ReadKind.Memory && impureOperands == 1);
        return IsAtom(initializer) || (uses == 1 && isPureOrSingleMemory);
    }

    private static bool IsAtom(ValueNode node) =>
        node is ConstantNode or RegisterNode or VariableReferenceNode or InstructionFieldNode { ResolvedNode: ConstantNode };

    private static ValueNode WithType(ValueNode node, DataType type) =>
        node.DataType == type ? node : new TypeConversionNode(type, node);

    private static ReadKind Classify(ValueNode node) => node switch {
        ConstantNode or RegisterNode or VariableReferenceNode => ReadKind.Pure,
        CpuFlagNode => ReadKind.Flag,
        InstructionFieldNode field => field.ResolvedNode is ConstantNode ? ReadKind.Pure : ReadKind.Memory,
        SegmentedPointerNode pointer => Worst(ReadKind.Memory, Worst(Classify(pointer.Segment), Classify(pointer.Offset))),
        AbsolutePointerNode pointer => Worst(ReadKind.Memory, Classify(pointer.AbsoluteAddress)),
        TypeConversionNode conversion => Classify(conversion.Value),
        UnaryOperationNode unary => Classify(unary.Value),
        BinaryOperationNode { BinaryOperation: not BinaryOperation.ASSIGN } binary =>
            Worst(Classify(binary.Left), Classify(binary.Right)),
        ConditionalValueNode conditional => Worst(Classify(conditional.Condition),
            Worst(Classify(conditional.WhenTrue), Classify(conditional.WhenFalse))),
        _ => ReadKind.Other
    };

    private static ValueNode WithoutSelfCancellation(ValueNode value) {
        if (value is BinaryOperationNode { BinaryOperation: BinaryOperation.BITWISE_XOR or BinaryOperation.MINUS } binary
            && binary.Left == binary.Right && Classify(binary.Left) == ReadKind.Pure) {
            return new ConstantNode(value.DataType, 0);
        }
        return value;
    }

    private static ReadKind Worst(ReadKind first, ReadKind second) => first > second ? first : second;

    private static int CountReferences(ValueNode node, VariableReferenceNode reference) => node switch {
        VariableReferenceNode other => other == reference ? 1 : 0,
        BinaryOperationNode binary => CountReferences(binary.Left, reference) + CountReferences(binary.Right, reference),
        ConditionalValueNode conditional => CountReferences(conditional.Condition, reference)
            + CountReferences(conditional.WhenTrue, reference)
            + CountReferences(conditional.WhenFalse, reference),
        TypeConversionNode conversion => CountReferences(conversion.Value, reference),
        ConstantNode => 0,
        CpuFlagNode => 0,
        _ => throw UnexpectedNode(node)
    };

    private static ValueNode Substitute(ValueNode node, Dictionary<VariableReferenceNode, ValueNode> replacements) => node switch {
        VariableReferenceNode reference => replacements.GetValueOrDefault(reference, reference),
        BinaryOperationNode binary => binary with {
            Left = Substitute(binary.Left, replacements),
            Right = Substitute(binary.Right, replacements)
        },
        TypeConversionNode conversion => conversion with { Value = Substitute(conversion.Value, replacements) },
        ConditionalValueNode conditional => conditional with {
            Condition = Substitute(conditional.Condition, replacements),
            WhenTrue = Substitute(conditional.WhenTrue, replacements),
            WhenFalse = Substitute(conditional.WhenFalse, replacements)
        },
        ConstantNode => node,
        CpuFlagNode => node,
        _ => throw UnexpectedNode(node)
    };

    private static InvalidOperationException UnexpectedNode(ValueNode node) =>
        new($"Unexpected {node.GetType().Name} in an ALU expansion");
}
