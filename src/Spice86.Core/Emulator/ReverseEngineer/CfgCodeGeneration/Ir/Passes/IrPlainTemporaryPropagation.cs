namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using System.Linq;

/// <summary>
/// Inlines temporary declarations inside a straight-line list of <see cref="IrStatement"/>s and
/// removes the declarations nothing reads.
///
/// <para>An initializer that is an atom (constant, register, segment register, flag read, temp
/// reference or constant instruction field) is substituted into every reader; a non-atom initializer
/// with no token read (<see cref="IrTokens"/>) and no call is substituted into every reader as well;
/// an initializer that reads a token (memory, port) is substituted only when it has exactly one
/// reader; an initializer with a call is never substituted. Every substitution wraps the initializer
/// in a <see cref="TypeConversionNode"/> when its type differs from the temp's declared type, and is
/// refused when a statement between the declaration and a reader may write a value the initializer
/// reads - a token-reading initializer additionally refuses a token write, a token read or a
/// faulting statement between the declaration and its reader, and a reader that evaluates another
/// token read before the temp's position (a device read can have an observable side effect, so the
/// order of two token reads is kept). The last reader itself does not count when it is
/// <c>x = e</c> (a register, flag or memory destination) with the temp read only inside <c>e</c> and
/// no call in <c>e</c>: its reads all happen before its single write.</para>
///
/// <para>A reader that follows a <c>R = t</c> statement (a <see cref="RegisterNode"/> of the temp's
/// type assigned exactly the temp reference) reads <c>R</c> instead, when no statement between them
/// may write <c>R</c>. This copy reuse runs before inlining, so a temp can drop to a single
/// reader.</para>
///
/// <para>A declaration no statement or terminator reads is removed when its stored effects are
/// removable (<see cref="IrStatement.IsRemovable"/>), and the removal repeats until nothing changes.
/// In an <see cref="IrMethod"/> a reader in another block, in another instruction or inside a nested
/// container still refuses the substitution and keeps the declaration.</para>
/// </summary>
internal static class IrPlainTemporaryPropagation {
    /// <summary>Propagates the temporaries inside every block of <paramref name="method"/>.</summary>
    public static void Apply(IrMethod method) {
        IrTemporaries temporaries = IrTemporaries.Build(method);
        foreach (IrBlock block in method.Blocks) {
            SimplifyStatements(block.Statements, block, temporaries);
        }
    }

    /// <summary>Simplifies one instruction's straight-line statement list with the same rules
    /// <see cref="Apply"/> uses on a block: own reader table, no terminator.</summary>
    internal static List<IrStatement> Simplify(IReadOnlyList<IrStatement> statements) {
        List<IrStatement> list = [.. statements];
        SimplifyStatements(list, null, null);
        return list;
    }

    /// <summary>Copy reuse first, then the inlining pass, then dead declarations until nothing
    /// changes. <paramref name="block"/> and <paramref name="temporaries"/> are set when the list is
    /// a method block: the terminator can read a temp and a read in another block refuses.</summary>
    private static void SimplifyStatements(List<IrStatement> statements, IrBlock? block,
        IrTemporaries? temporaries) {
        ReuseCopies(statements);
        for (int i = 0; i < statements.Count; i++) {
            if (statements[i].Node is VariableDeclarationNode declaration
                && Propagated(statements, block, temporaries, i, declaration)) {
                statements.RemoveAt(i);
                i--;
            }
        }
        RemoveDeadDeclarations(statements, block, temporaries);
    }

    /// <summary>Redirects the readers of a temp that follow a <c>R = t</c> copy statement to
    /// <c>R</c>, when <c>R</c> has the temp's type and no statement between the copy and the reader
    /// may write it.</summary>
    private static void ReuseCopies(List<IrStatement> statements) {
        HashSet<string> declared = statements
            .Select(statement => statement.Node)
            .OfType<VariableDeclarationNode>()
            .Select(declaration => declaration.VariableName)
            .ToHashSet();
        for (int i = 0; i < statements.Count; i++) {
            if (statements[i].Node is not BinaryOperationNode {
                    BinaryOperation: BinaryOperation.ASSIGN,
                    Left: RegisterNode target,
                    Right: VariableReferenceNode source }
                || target.DataType != source.DataType
                || !declared.Contains(source.VariableName)) {
                continue;
            }
            CpuValues registerWrites = IrValueSets.Of(target);
            for (int j = i + 1; j < statements.Count; j++) {
                IrStatement reader = statements[j];
                if ((reader.Effects.MayWrite & registerWrites) != CpuValues.None) {
                    break;
                }
                if (!reader.Origin.Equals(statements[i].Origin)
                    || !ContainsVariable(reader.Node, source.VariableName)) {
                    continue;
                }
                if (Substitute(reader.Node, source.VariableName, target) is { } rewritten) {
                    statements[j] = IrStatement.Create(rewritten, reader.Origin);
                }
            }
        }
    }

    private static bool Propagated(List<IrStatement> statements, IrBlock? block,
        IrTemporaries? temporaries, int index, VariableDeclarationNode declaration) {
        string name = declaration.VariableName;
        ICfgNode origin = statements[index].Origin;
        List<int> readers = [];
        bool terminatorReads = block is { HasTerminator: true }
            && ContainsVariable(block.Terminator, name);
        for (int i = index + 1; i < statements.Count; i++) {
            IVisitableAstNode node = statements[i].Node;
            if (!ContainsVariable(node, name)) {
                continue;
            }
            if (!statements[i].Origin.Equals(origin) || AssignsVariable(node, name)
                || HasNestedReference(node, name)) {
                return false;
            }
            readers.Add(i);
        }
        if (terminatorReads && block is { HasTerminator: true }
            && !block.Terminator.Origin.Equals(origin)) {
            return false;
        }
        if (ReadOutside(temporaries, block, name)) {
            return false;
        }
        int readerCount = readers.Count + (terminatorReads ? 1 : 0);
        if (readerCount == 0) {
            return false;
        }
        ValueNode initializer = declaration.Initializer;
        IrEffects initializerEffects = IrEffectsVisitor.Of(initializer, false);
        if (initializerEffects.MayWrite != CpuValues.None || ContainsCall(initializer)) {
            return false;
        }
        bool tokenRead = initializerEffects.TokenReads != IrTokens.None;
        if (tokenRead && readerCount != 1) {
            return false;
        }
        int lastChecked = terminatorReads ? statements.Count - 1 : readers[^1];
        for (int i = index + 1; i <= lastChecked; i++) {
            if (!terminatorReads && i == lastChecked && !statements[i].Effects.MayFault
                && AssignsAfterReading(statements[i].Node, name)) {
                continue;
            }
            if (!WritableBetween(statements[i].Effects, initializerEffects, tokenRead,
                    i == lastChecked)) {
                return false;
            }
        }
        if (terminatorReads && block is { HasTerminator: true }
            && (!WritableBetween(block.Terminator.Effects, initializerEffects, tokenRead, true)
                || (tokenRead && TerminatorReadsTokenBefore(block.Terminator, name)))) {
            return false;
        }
        if (tokenRead && readers.Any(reader => ReadsTokenBefore(statements[reader].Node, name))) {
            return false;
        }
        return SubstituteReaders(statements, block, readers, terminatorReads, index, declaration,
            initializer);
    }

    /// <summary>A reader <c>x = e</c> whose own write is no obstacle to the substitution: the temp is
    /// read only inside <c>e</c>, which holds no call, so every read of the initializer still happens
    /// where the statement's reads happen - before the write of the register, flag or memory
    /// destination.</summary>
    private static bool AssignsAfterReading(IVisitableAstNode node, string name) =>
        node is BinaryOperationNode {
            BinaryOperation: BinaryOperation.ASSIGN,
            Left: RegisterNode or CpuFlagNode or AbsolutePointerNode or SegmentedPointerNode,
            Right: ValueNode value } assign
        && ContainsVariable(value, name) && !ContainsVariable(assign.Left, name)
        && !ContainsCall(node);

    /// <summary>Whether <paramref name="between"/> lets the initializer move past it: it does not
    /// write a value the initializer reads, and for a token-reading initializer it writes no token
    /// and cannot fault. A statement strictly between the declaration and the reader must not read
    /// a token either - the order of two device reads is observable; the reader's own token reads
    /// are checked positionally by <see cref="ReadsTokenBefore"/>.</summary>
    private static bool WritableBetween(IrEffects between, IrEffects initializer, bool tokenRead,
        bool isLastReader) =>
        (between.MayWrite & initializer.Reads) == CpuValues.None
        && (!tokenRead || TokenSafeBetween(between, isLastReader));

    /// <summary>For a token-reading initializer: the statement writes no token, cannot fault,
    /// and reads no token unless it is the last reader.</summary>
    private static bool TokenSafeBetween(IrEffects between, bool isLastReader) =>
        between.TokenWrites == IrTokens.None && !between.MayFault
        && (isLastReader || between.TokenReads == IrTokens.None);

    /// <summary>Whether <paramref name="node"/> evaluates a token read before the first reference
    /// of the temp <paramref name="name"/> in C# evaluation order: children are visited left to
    /// right and a subtree that reads a token without holding the temp runs entirely before it.</summary>
    private static bool ReadsTokenBefore(IVisitableAstNode node, string name) {
        foreach (IVisitableAstNode child in EvaluationChildren(node)) {
            if (ContainsVariable(child, name)) {
                return ReadsTokenBefore(child, name);
            }
            if (IrEffectsVisitor.Of(child, false).TokenReads != IrTokens.None) {
                return true;
            }
        }
        return false;
    }

    /// <summary>The children of <paramref name="node"/> in the order C# evaluates them: an
    /// assignment to a pointer target evaluates the address parts before the value (the target
    /// itself is the write, not a read); every other node takes its plain children order.</summary>
    private static IEnumerable<IVisitableAstNode> EvaluationChildren(IVisitableAstNode node) {
        if (node is not BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
                Left: { } left, Right: { } right }) {
            return IrAstChildren.ChildrenOf(node);
        }
        if (left is SegmentedPointerNode or AbsolutePointerNode) {
            return IrAstChildren.ChildrenOf(left).Append(right);
        }
        return [right];
    }

    private static bool TerminatorReadsTokenBefore(IrTerminator terminator, string name) =>
        terminator switch {
            IrBranch branch => ReadsTokenBefore(branch.Condition, name),
            IrSwitch @switch => ReadsTokenBefore(@switch.Value, name),
            IrExit exit => ReadsTokenBefore(exit.Transfer, name),
            _ => false
        };

    private static bool SubstituteReaders(List<IrStatement> statements, IrBlock? block,
        List<int> readers, bool terminatorReads, int index, VariableDeclarationNode declaration,
        ValueNode initializer) {
        ValueNode typed = WithType(initializer, declaration.DataType);
        Dictionary<int, IVisitableAstNode> rewritten = new();
        foreach (int reader in readers) {
            if (Substitute(statements[reader].Node, declaration.VariableName, typed) is not { } node) {
                return false;
            }
            rewritten[reader] = node;
        }
        IrTerminator? rewrittenTerminator = null;
        if (terminatorReads && block is { HasTerminator: true }) {
            if (Substitute(block.Terminator, declaration.VariableName, typed)
                is not { } terminator) {
                return false;
            }
            rewrittenTerminator = terminator;
        }
        foreach ((int reader, IVisitableAstNode node) in rewritten) {
            statements[reader] = IrStatement.Create(node, statements[reader].Origin);
        }
        if (rewrittenTerminator is not null && block is { HasTerminator: true }) {
            block.Terminator = rewrittenTerminator;
        }
        return true;
    }

    /// <summary>Removes the declarations nothing reads when their effects are removable, until no
    /// removal frees another temp's last reader.</summary>
    private static void RemoveDeadDeclarations(List<IrStatement> statements, IrBlock? block,
        IrTemporaries? temporaries) {
        bool removed;
        do {
            removed = false;
            for (int i = 0; i < statements.Count; i++) {
                if (statements[i].Node is VariableDeclarationNode declaration
                    && statements[i].IsRemovable
                    && !HasReader(statements, block, temporaries, declaration.VariableName)) {
                    statements.RemoveAt(i);
                    i--;
                    removed = true;
                }
            }
        } while (removed);
    }

    private static bool HasReader(List<IrStatement> statements, IrBlock? block,
        IrTemporaries? temporaries, string name) {
        if (statements.Any(statement => ContainsVariable(statement.Node, name))) {
            return true;
        }
        if (block is { HasTerminator: true } && ContainsVariable(block.Terminator, name)) {
            return true;
        }
        return temporaries is not null && block is not null
            && temporaries.ReadsOf(name).Any(read => !ReferenceEquals(read.Block, block));
    }

    private static ValueNode WithType(ValueNode node, DataType type) =>
        node.DataType == type ? node : new TypeConversionNode(type, node);

    private static bool ContainsCall(IVisitableAstNode node) =>
        IrAstChildren.Descendants(node).Any(descendant => descendant is MethodCallValueNode);

    /// <summary>The variable escapes to another block: the substitution cannot reach those reads.</summary>
    private static bool ReadOutside(IrTemporaries? temporaries, IrBlock? block, string name) =>
        temporaries is not null && block is not null
        && temporaries.ReadsOf(name).Any(read => !ReferenceEquals(read.Block, block));

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
                or MultiplicationNode or TupleSwapNode or FailAsUntestedNode or BlockNode or IfElseNode or WhileNode
                or WhileWithExitNode or InstructionPointerNode or LoadOffsetParameterNode
                or SegmentVariableNode or OffsetLiteralNode or SignatureFieldNode or MethodGroupNode
                or SelectorNode or SetInterruptShadowNode or ThrowNode or CyclicJumpNode
                or LocalDeclarationNode or InterruptReturnNode or PreTestLoopNode or DoWhileNode
                or InfiniteLoopNode or SwitchNode or CpuFaultTryCatchNode or InstructionBoundaryNode
                or InstructionNode or CfgInstructionNode or RegisterLocalNode
                or RegisterWriteBackNode:
                return null;
            default:
                return IrAstChildren.TryMapChildren(node, child => Substitute(child, name, value));
        }
    }
}
