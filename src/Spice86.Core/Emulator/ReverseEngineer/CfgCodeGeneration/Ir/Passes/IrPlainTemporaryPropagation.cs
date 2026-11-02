namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using System.Linq;

/// <summary>
/// Inlines plain temporary declarations inside each block of an <see cref="IrMethod"/>: when a
/// <see cref="VariableDeclarationNode"/> initializer contains no memory access, call or variable, and
/// the declared variable is only read by later statements (or the terminator) of the same block
/// lifted from the same instruction, the initializer expression is substituted at each read and the
/// declaration is removed. Substitution is refused when the variable is assigned again, read where
/// the substitution cannot reach (another block, a statement of another instruction, a nested
/// container), or a statement between the declaration and a reader may write a value the
/// initializer reads.
/// </summary>
internal static class IrPlainTemporaryPropagation {
    /// <summary>Propagates plain temporaries inside every block of <paramref name="method"/>.</summary>
    public static void Apply(IrMethod method) {
        IrTemporaries temporaries = IrTemporaries.Build(method);
        foreach (IrBlock block in method.Blocks) {
            ApplyInBlock(temporaries, block);
        }
    }

    private static void ApplyInBlock(IrTemporaries temporaries, IrBlock block) {
        List<IrStatement> statements = block.Statements;
        for (int i = 0; i < statements.Count; i++) {
            if (statements[i].Node is VariableDeclarationNode declaration
                && Propagated(temporaries, block, i, declaration)) {
                statements.RemoveAt(i);
                i--;
            }
        }
    }

    private static bool Propagated(IrTemporaries temporaries, IrBlock block, int index,
        VariableDeclarationNode declaration) {
        if (!IsPlain(declaration.Initializer)) {
            return false;
        }
        string name = declaration.VariableName;
        ICfgNode origin = block.Statements[index].Origin;
        List<IrStatement> statements = block.Statements;
        int lastReference = -1;
        bool terminatorReads = block.Terminator is not null && ContainsVariable(block.Terminator, name);
        for (int i = index + 1; i < statements.Count; i++) {
            IVisitableAstNode node = statements[i].Node;
            bool reads = ContainsVariable(node, name);
            if (!reads) {
                continue;
            }
            if (!statements[i].Origin.Equals(origin) || AssignsVariable(node, name)
                || HasNestedReference(node, name)) {
                return false;
            }
            lastReference = i;
        }
        if (block.Terminator is not null && terminatorReads && !block.Terminator.Origin.Equals(origin)) {
            return false;
        }
        if (lastReference < 0 && !terminatorReads) {
            return false;
        }
        if (ReadOutside(temporaries, block, name)) {
            return false;
        }
        IrValues initializerReads = IrEffectsVisitor.Of(declaration.Initializer, false).Reads;
        int lastChecked = terminatorReads ? statements.Count - 1 : lastReference;
        for (int i = index + 1; i <= lastChecked; i++) {
            if ((statements[i].Effects.MayWrite & initializerReads) != IrValues.None) {
                return false;
            }
        }
        if (terminatorReads
            && block.Terminator is not null
            && (block.Terminator.Effects.MayWrite & initializerReads) != IrValues.None) {
            return false;
        }
        for (int i = index + 1; i <= lastReference; i++) {
            if (ContainsVariable(statements[i].Node, name)) {
                if (Substitute(statements[i].Node, name, declaration.Initializer) is not { } substituted) {
                    return false;
                }
                statements[i] = IrStatement.Create(substituted, statements[i].Origin);
            }
        }
        if (terminatorReads && block.Terminator is not null) {
            if (Substitute(block.Terminator, name, declaration.Initializer) is not { } rewritten) {
                return false;
            }
            block.Terminator = rewritten;
        }
        return true;
    }

    /// <summary>The variable escapes to another block: the substitution cannot reach those reads.</summary>
    private static bool ReadOutside(IrTemporaries temporaries, IrBlock block, string name) =>
        temporaries.ReadsOf(name).Any(read => !ReferenceEquals(read.Block, block));

    /// <summary>A reference to the variable under a nested statement container of one statement.</summary>
    private static bool HasNestedReference(IVisitableAstNode statement, string name) =>
        statement is IfElseNode or WhileNode or WhileWithExitNode && ContainsVariable(statement, name);

    private static bool AssignsVariable(IVisitableAstNode statement, string name) =>
        statement is BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN } assign
            && assign.Left is VariableReferenceNode reference
            && reference.VariableName == name;

    private static bool ContainsVariable(IrTerminator terminator, string name) => terminator switch {
        IrJump => false,
        IrBranch branch => ContainsVariable(branch.Condition, name),
        IrSwitch @switch => ContainsVariable(@switch.Value, name),
        IrExit exit => ContainsVariable(exit.Transfer, name),
        _ => true
    };

    private static IrTerminator? Substitute(IrTerminator terminator, string name, ValueNode value) =>
        terminator switch {
            IrJump => terminator,
            IrBranch branch => Substitute(branch.Condition, name, value) is ValueNode condition
                ? new IrBranch(branch.Origin, condition, branch.WhenTrue, branch.WhenFalse)
                : null,
            IrSwitch @switch => Substitute(@switch.Value, name, value) is ValueNode switched
                ? new IrSwitch(@switch.Origin, switched, @switch.Cases, @switch.Default)
                : null,
            IrExit exit => Substitute(exit.Transfer, name, value) is { } transfer
                ? new IrExit(exit.Origin, transfer)
                : null,
            _ => null
        };

    /// <summary>Whether the node reads the variable. A node shape the walker does not know throws:
    /// the enumeration must cover every node type before a reference can be stranded.</summary>
    private static bool ContainsVariable(IVisitableAstNode node, string name) =>
        IrAstChildren.Descendants(node)
            .Any(descendant => descendant is VariableReferenceNode reference
                && reference.VariableName == name);

    private static IVisitableAstNode? Substitute(IVisitableAstNode node, string name, ValueNode value) {
        switch (node) {
            case VariableReferenceNode reference:
                return reference.VariableName == name ? value : reference;
            case InstructionFieldNode field:
                return ContainsVariable(field.ResolvedNode, name) ? null : field;
            case ExplicitCastNode or SegmentedAddressNode or OpaqueNode or DivisionNode
                or TupleSwapNode or FailAsUntestedNode or BlockNode or IfElseNode or WhileNode
                or WhileWithExitNode or InstructionPointerNode or LoadOffsetParameterNode
                or SegmentVariableNode or OffsetLiteralNode or SignatureFieldNode or MethodGroupNode
                or SelectorNode or SetInterruptShadowNode or ThrowNode or CyclicJumpNode
                or LocalDeclarationNode or InterruptReturnNode or PreTestLoopNode or DoWhileNode
                or InfiniteLoopNode or SwitchNode or CpuFaultTryCatchNode or InstructionBoundaryNode
                or InstructionNode or CfgInstructionNode:
                return null;
            default:
                return IrAstChildren.TryMapChildren(node, child => Substitute(child, name, value));
        }
    }

    /// <summary>Registers, constants, conversions, unary and non-assignment binary operations only.</summary>
    private static bool IsPlain(ValueNode node) =>
        IrAstChildren.Descendants(node).All(descendant => descendant switch {
            ConstantNode or RegisterNode or SegmentRegisterNode or InstructionFieldNode
                or TypeConversionNode or UnaryOperationNode => true,
            BinaryOperationNode binary => binary.BinaryOperation != BinaryOperation.ASSIGN,
            _ => false
        });
}
