namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using System.Linq;

/// <summary>
/// Replaces every read of <c>CpuFlagNode(Carry)</c> in the statements of a plain
/// <see cref="AluAssignmentRewrite"/> output that come before the first statement writing the carry flag —
/// the reads in that statement's right side included, since they still see the old value. A shape the
/// rewrite does not produce stops the substitution and the node keeps its original AST.
/// </summary>
internal static class FlagReadSubstitution {
    /// <summary>The substituted block, or <c>null</c> when a statement or expression shape is not covered.</summary>
    public static BlockNode? SubstituteCarry(BlockNode plain, ValueNode relation) {
        List<IVisitableAstNode> statements = new();
        foreach (IVisitableAstNode statement in plain.Statements) {
            IVisitableAstNode? substituted = SubstituteStatement(statement, relation);
            if (substituted is null) {
                return null;
            }
            statements.Add(substituted);
            if (WritesCarry(statement)) {
                statements.AddRange(plain.Statements.Skip(statements.Count));
                return new BlockNode([.. statements]);
            }
        }
        return new BlockNode([.. statements]);
    }

    private static bool WritesCarry(IVisitableAstNode statement) {
        FlagEffect effect = FlagEffectVisitor.Of(statement);
        return (effect.MustWrite & StatusFlags.Carry) != StatusFlags.None
            || (effect.MayWrite & StatusFlags.Carry) != StatusFlags.None;
    }

    private static IVisitableAstNode? SubstituteStatement(IVisitableAstNode statement, ValueNode relation) =>
        statement switch {
            BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN } assign =>
                SubstituteLeft(assign.Left, relation) is { } left
                && SubstituteValue(assign.Right, relation) is { } right
                    ? assign with { Left = left, Right = right }
                    : null,
            VariableDeclarationNode declaration =>
                SubstituteValue(declaration.Initializer, relation) is { } initializer
                    ? declaration with { Initializer = initializer }
                    : null,
            MethodCallNode or MoveIpNextNode => statement,
            _ => null
        };

    /// <summary>The assign target is never substituted when it is a bare flag node: a flag on the left is the
    /// write itself, not a read. Anything else (a pointer write's address) is substituted like an expression.</summary>
    private static ValueNode? SubstituteLeft(ValueNode left, ValueNode relation) =>
        left is CpuFlagNode ? left : SubstituteValue(left, relation);

    private static ValueNode? SubstituteValue(ValueNode node, ValueNode relation) {
        switch (node) {
            case CpuFlagNode flag:
                return flag.FlagMask == Flags.Carry ? relation : flag;
            case ConstantNode or RegisterNode or SegmentRegisterNode or VariableReferenceNode
                or InstructionFieldNode or FlagRegisterNode:
                return node;
            case TypeConversionNode or UnaryOperationNode or SegmentedPointerNode or AbsolutePointerNode
                or MethodCallValueNode or BinaryOperationNode { BinaryOperation: not BinaryOperation.ASSIGN }:
                return (ValueNode?)IrAstChildren.TryMapChildren(node, child => child is ValueNode value
                    ? SubstituteValue(value, relation)
                    : null);
            default:
                return null;
        }
    }
}
