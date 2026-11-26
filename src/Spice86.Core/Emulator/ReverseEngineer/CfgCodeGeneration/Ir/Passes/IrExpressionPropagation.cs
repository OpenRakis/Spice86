namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;

using System.Linq;

/// <summary>
/// A register version produced by a plain assignment and
/// read exactly once is merged into its reader, so one statement covers several instructions:
/// <c>add bx,1</c> followed by <c>shr bx,1</c> emits <c>BX = (ushort)((ushort)(BX + 1) &gt;&gt; 1);</c>.
/// Legality is <see cref="IrSsa.SameVersions"/> over the moved expression's register and IO-token
/// reads plus no faulting or guard statement between the writer and the reader, and for the memory
/// token either the version is the same or every memory-writing statement in between is a plain
/// store or a push (<see cref="IrMemoryAliasAnalysis.IsFullMemoryBoundary"/> refuses the rest) whose
/// writes are all <see cref="IrAliasRelation.Disjoint"/> known RAM from every memory read the moved
/// expression makes. A read not proved conventional RAM also keeps its order with other such
/// reads (<see cref="IrMemoryAliasAnalysis.DeviceReadOrderSafe"/>): the version rule cannot see
/// a device read's side effect. An event check needs no refusal of its own: its may-write re-versions the
/// registers a handler can observe, so the version rule already blocks a merge across it.
/// </summary>
internal static class IrExpressionPropagation {
    /// <summary>The AST-node budget of one merged expression.</summary>
    private const int MaxMergedNodes = 16;
    /// <summary>How many instructions one statement may cover.</summary>
    private const int MaxChainLength = 3;

    /// <summary>The <see cref="IrSsaValue"/> bit set of the general and segment registers.</summary>
    private const uint RegisterValues = (1u << (int)IrSsaValue.Gs + 1) - 1;

    /// <summary>Merges the single-use register writes of <paramref name="method"/> into their readers.</summary>
    /// <summary>Applies the pass in strict mode.</summary>
    public static void Apply(IrMethod method, IrEditor editor) =>
        Apply(method, editor, GenerationPolicy.Strict);

    public static void Apply(IrMethod method, IrEditor editor, GenerationPolicy policy) {
        HashSet<ICfgNode> entryNodes = method.Entries.Select(entry => entry.Node).ToHashSet();
        IrTemporaries temporaries = IrTemporaries.Build(method);
        foreach (IrBlock block in method.Blocks) {
            if (editor.Ssa.IsStale) {
                editor.Rebind(method, "ssa rebind expression propagation");
            }
            MergeBlock(block, method, entryNodes, editor.Ssa, editor,
                new IrMemoryAliasAnalysis(method, editor.Ssa, policy), temporaries, policy);
        }
    }

    private static void MergeBlock(IrBlock block, IrMethod method, HashSet<ICfgNode> entryNodes,
        IrSsa ssa, IrEditor editor, IrMemoryAliasAnalysis alias, IrTemporaries temporaries,
        GenerationPolicy policy) {
        if (ssa.IsLivenessStale) {
            ssa.RecomputeLiveness();
        }
        int[] covered = new int[block.Statements.Count];
        Array.Fill(covered, 1);
        List<int> removed = [];
        for (int i = 0; i < block.Statements.Count; i++) {
            if (ssa.IsStale) {
                ssa = editor.Rebind(method, "ssa rebind expression propagation");
                alias = new IrMemoryAliasAnalysis(method, ssa, policy);
                temporaries = IrTemporaries.Build(method);
            }
            if (covered[i] > 0) {
                if (TryMerge(block, i, entryNodes, ssa, editor, covered, alias) is not null) {
                    removed.Add(i);
                } else if (TryComposeHalves(block, i, entryNodes, ssa, editor, covered, alias)
                    is { } composedWriters) {
                    removed.AddRange(composedWriters);
                } else if (TryMergeTemporary(block, method, i, entryNodes, ssa, editor, covered,
                    alias, temporaries) is { } tempRemoved) {
                    removed.Add(i);
                    removed.AddRange(tempRemoved);
                }
            }
        }
        foreach (int index in removed.OrderByDescending(index => index)) {
            editor.Remove(block, index);
        }
    }

    /// <summary>A register version produced by a plain assignment whose only live use is a typed
    /// method's return exit merges into the return element: <c>AX = e; NearReturn(); return AX;</c>
    /// prints <c>NearReturn(); return e;</c> and the assignment goes. The legality is the merge's
    /// own: the writer shape is <see cref="SingleRegisterWriter"/>'s, the moved expression's
    /// register and IO-token reads keep their versions up to the exit, memory order holds,
    /// and no faulting or guard statement lies between the writer and the exit - which
    /// itself cannot fault. Whether the read is really an output element of a typed signature is
    /// the caller's check: this returns the writer, the register and the merged expression.</summary>
    internal static (IrStatement Writer, RegisterNode Register, ValueNode Expression,
            ProcedureReturnHelperNode Helper)? MergedIntoReturn(IrBlock block, int i,
            HashSet<ICfgNode> entryNodes, IrSsa ssa, IrMemoryAliasAnalysis alias) {
        IrStatement writer = block.Statements[i];
        if (SingleRegisterWriter(writer, entryNodes, ssa) is not
                (RegisterNode r1, ValueNode e1, _, int writerVersion)
            || ssa.LiveUses(writerVersion) is not [IrSsaUse {
                    Phi: null, Statement: null, Block: var useBlock,
                    Terminator: IrExit {
                        Transfer: ReturnNode { Value: ProcedureReturnHelperNode helper } } exit }]
            || useBlock != block
            || !ReferenceEquals(exit, block.Terminator)
            || exit.Effects.MayFault) {
            return null;
        }
        int end = block.Statements.Count;
        for (int k = i; k < end; k++) {
            IrStatement between = block.Statements[k];
            if (between.Effects.MayFault
                || between.Node is SpeculativeGuardNode) {
                return null;
            }
        }
        IrEffects moved = IrEffectsVisitor.Of(e1, false);
        // A self-read (AX = AX + 1) cannot merge: the version check anchors after the writer, where
        // the merged register already holds its new version, so the moved expression would evaluate
        // the change a second time at the exit.
        if ((moved.Reads & IrValueSets.Of(r1)) != CpuValues.None) {
            return null;
        }
        // The printed return expression evaluates after the return helper statement ran: a near or
        // far `ret` already popped the return address (and CS for a far one), so a moved read of SP -
        // directly or through a stack-addressed operand like a frame argument - would see the
        // post-return value.
        if ((moved.Reads & (helper.Distance == CallDistance.Far
                    ? CpuValues.Sp | CpuValues.Cs : CpuValues.Sp)) != CpuValues.None) {
            return null;
        }
        IrTokens tokens = moved.TokenReads & ~IrTokens.Memory;
        if (moved.TouchesMemory) {
            tokens |= IrTokens.Io;
        }
        if (!ssa.SameVersions(moved.Reads, tokens, (block, i + 1), (block, end))
            || !MemoryCrossable(block, i, end, moved, ssa, alias)) {
            return null;
        }
        ValueNode merged = (ValueNode)IrConstantFolder.FoldConstantTerms(e1);
        if (CountNodes(merged) > MaxMergedNodes) {
            return null;
        }
        return (writer, r1, merged, helper);
    }

    /// <summary>The shared writer shape of <see cref="TryMerge"/> and
    /// <see cref="MergedIntoReturn"/>: a plain <c>R = e</c> assignment of a mergeable expression
    /// to a general register (not a segment, not SP) at a non-entry origin, defining exactly one
    /// version of one register value and nothing else still live.</summary>
    private static (RegisterNode Register, ValueNode Expression, IrSsaValue Value, int Version)?
        SingleRegisterWriter(IrStatement writer, HashSet<ICfgNode> entryNodes, IrSsa ssa) {
        if (entryNodes.Contains(writer.Origin)
            || writer.Node is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN,
                Left: RegisterNode r1,
                Right: ValueNode e1
            }
            || r1 is SegmentRegisterNode
            || (IrValueSets.Of(r1) & CpuValues.Sp) != CpuValues.None
            || !Mergeable(e1)) {
            return null;
        }
        uint r1Bits = IrSsaValues.ValueBits(IrValueSets.Of(r1));
        if (r1Bits == 0 || (r1Bits & (r1Bits - 1)) != 0) {
            return null;
        }
        IrSsaValue r1Value = (IrSsaValue)System.Numerics.BitOperations.TrailingZeroCount(r1Bits);
        int writerVersion = -1;
        uint definedRegisters = 0;
        foreach ((IrSsaValue value, int version) in ssa.Definitions(writer)) {
            if (value == r1Value) {
                writerVersion = version;
            } else if ((RegisterValues & (1u << (int)value)) != 0) {
                definedRegisters |= 1u << (int)value;
            } else if (value < IrSsaValue.Memory && ssa.IsLive(version)) {
                return null;
            } else if (value >= IrSsaValue.Memory) {
                return null;
            }
        }
        return writerVersion < 0 || definedRegisters != 0 ? null : (r1, e1, r1Value, writerVersion);
    }

    /// <summary>Merges the assignment of statement <paramref name="i"/> into the only statement reading
    /// its register when every condition holds; returns the reader's index.</summary>
    private static int? TryMerge(IrBlock block, int i, HashSet<ICfgNode> entryNodes, IrSsa ssa,
        IrEditor editor, int[] covered, IrMemoryAliasAnalysis alias) {
        IrStatement writer = block.Statements[i];
        if (SingleRegisterWriter(writer, entryNodes, ssa) is not
                (RegisterNode r1, ValueNode e1, IrSsaValue r1Value, int writerVersion)) {
            return null;
        }
        uint r1Bits = IrSsaValues.ValueBits(IrValueSets.Of(r1));
        if (ssa.LiveUses(writerVersion) is not [IrSsaUse { Phi: null, Statement: not null }]) {
            return null;
        }
        int j = ReaderIndex(block, r1Value, writerVersion, i, ssa);
        if (j < 0) {
            return null;
        }
        IrStatement reader = block.Statements[j];
        if (entryNodes.Contains(reader.Origin)) {
            return null;
        }
        for (int k = i; k <= j; k++) {
            IrStatement between = block.Statements[k];
            if (between.Effects.MayFault
                || between.Node is SpeculativeGuardNode) {
                return null;
            }
        }
        if (reader.Node is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN,
                Left: ValueNode r2,
                Right: ValueNode e2
            } assign
            || !Mergeable(e2)) {
            return null;
        }
        List<RegisterNode> expressionRegisters = [];
        CollectRegisters(e2, expressionRegisters);
        if (expressionRegisters.Count(register => register.Equals(r1)) != 1
            || expressionRegisters.Any(register => !register.Equals(r1) && Overlaps(register, r1))) {
            return null;
        }
        List<RegisterNode> addressRegisters = [];
        CollectAddressRegisters(r2, addressRegisters);
        if (addressRegisters.Any(register => Overlaps(register, r1)) || !MergeableTarget(r2)) {
            return null;
        }
        // A reader that partially defines r1's value also uses the writer's version implicitly, to
        // keep the bytes its target does not write. Substitute cannot rewrite that link, so the
        // merge may run only when the target rewrites every byte r1 wrote.
        CpuValues r2Mask = r2 is RegisterNode targetRegister ? IrValueSets.Of(targetRegister) : CpuValues.None;
        if ((IrSsaValues.WriteBits(reader.Effects).Partial & r1Bits) != 0
            && (IrValueSets.Of(r1) & ~r2Mask) != CpuValues.None) {
            return null;
        }
        IrEffects moved = IrEffectsVisitor.Of(e1, false);
        IrTokens tokens = moved.TokenReads & ~IrTokens.Memory;
        if (moved.TouchesMemory) {
            tokens |= IrTokens.Io;
        }
        // Compare "after the writer" (before the next statement) with "before the reader": the merged
        // register itself legitimately changes at the writer - that is the merge. Anything else
        // redefined in between refuses; a changed memory version still merges when the moved read is
        // proved disjoint from every write in between.
        if (!ssa.SameVersions(moved.Reads, tokens, (block, i + 1), (block, j))
            || !MemoryCrossable(block, i, j, moved, ssa, alias)) {
            return null;
        }
        ValueNode rawMerged = Substitute(e2, r1, e1);
        ValueNode merged = (ValueNode)IrConstantFolder.FoldConstantTerms(rawMerged);
        if (CountNodes(merged) > MaxMergedNodes
            || (covered[i] + covered[j] > MaxChainLength
                && CountNodes(merged) >= CountNodes(rawMerged))) {
            return null;
        }
        IrStatement replacement = IrStatement.Create(assign with { Right = merged }, reader.Origin);
        editor.Replace(block, j, replacement);
        if (!ssa.IsStale) {
            ssa.RecomputeLiveness();
        }
        covered[j] += covered[i];
        covered[i] = 0;
        return j;
    }

    /// <summary>A single-use variable declaration <c>uint t = e;</c> (a temporary the
    /// double-precision and temporary passes produce, which this pass runs after and
    /// <see cref="IrPlainTemporaryPropagation"/> runs before) is substituted into every reader when
    /// each read is a later statement of the same block, the variable is read nowhere else -
    /// another block or the terminator - and the move keeps the merge's own legality: a mergeable
    /// initializer, no faulting or guard statement between the declaration and its last reader,
    /// every register and IO-token read of the initializer keeps its version and memory order
    /// holds to each reader.
    /// Temporaries are single-assignment, so no statement between them can rewrite a variable the
    /// initializer reads.</summary>
    private static List<int>? TryMergeTemporary(IrBlock block, IrMethod method, int i,
        HashSet<ICfgNode> entryNodes, IrSsa ssa, IrEditor editor, int[] covered,
        IrMemoryAliasAnalysis alias, IrTemporaries temporaries) {
        IrStatement writer = block.Statements[i];
        if (entryNodes.Contains(writer.Origin)
            || writer.Node is not VariableDeclarationNode declaration
            || !Mergeable(declaration.Initializer)) {
            return null;
        }
        List<int> readers = [];
        foreach ((IrBlock readBlock, int index) in temporaries.ReadsOf(declaration.VariableName)) {
            if (!ReferenceEquals(readBlock, block)
                || index <= i
                || index >= block.Statements.Count) {
                return null;
            }
            readers.Add(index);
        }
        if (readers.Count == 0) {
            return null;
        }
        foreach (int j in readers) {
            if (entryNodes.Contains(block.Statements[j].Origin)
                || CountVariableReferences(block.Statements[j].Node, declaration.VariableName)
                    != 1) {
                return null;
            }
        }
        // A reader that only writes the temp's value into a dead register version goes away with
        // the declaration: the pair's wide write rewrites the register before anything reads it,
        // so the substituted initializer must keep the version the declaration evaluated, not the
        // one the dead write left.
        HashSet<int> deadReaders = [];
        foreach (int j in readers) {
            if (DeadWriter(block.Statements[j], ssa)) {
                deadReaders.Add(j);
            }
        }
        List<int> liveReaders = [.. readers.Where(j => !deadReaders.Contains(j))];
        int lastReader = liveReaders.Count == 0 ? readers[^1] : liveReaders[^1];
        for (int k = i; k <= lastReader; k++) {
            IrStatement between = block.Statements[k];
            if (between.Effects.MayFault
                || between.Node is SpeculativeGuardNode) {
                return null;
            }
        }
        IrEffects moved = IrEffectsVisitor.Of(declaration.Initializer, false);
        IrTokens tokens = moved.TokenReads & ~IrTokens.Memory;
        if (moved.TouchesMemory) {
            tokens |= IrTokens.Io;
        }
        foreach (int j in liveReaders) {
            bool registerWritesSafe = true;
            for (int k = i + 1; k < j; k++) {
                if (!deadReaders.Contains(k)
                    && (block.Statements[k].Effects.MayWrite & moved.Reads)
                        != CpuValues.None) {
                    registerWritesSafe = false;
                    break;
                }
            }
            if (!registerWritesSafe
                || !ssa.SameVersions(CpuValues.None, tokens, (block, i + 1), (block, j))
                || !MemoryCrossable(block, i, j, moved, ssa, alias)) {
                return null;
            }
        }
        ValueNode typed = declaration.Initializer.DataType == declaration.DataType
            ? declaration.Initializer
            : new TypeConversionNode(declaration.DataType, declaration.Initializer);
        if (CountNodes(typed) > MaxMergedNodes) {
            return null;
        }
        List<(int Index, IVisitableAstNode Node)> merged = [];
        foreach (int j in liveReaders) {
            if (SubstituteVariable(block.Statements[j].Node, declaration.VariableName, typed)
                    is not { } rawMerged
                || IrConstantFolder.FoldConstantTerms(rawMerged) is not { } rewritten
                || covered[i] + covered[j] > MaxChainLength) {
                return null;
            }
            merged.Add((j, rewritten));
        }
        foreach ((int j, IVisitableAstNode rewritten) in merged) {
            editor.Replace(block, j,
                IrStatement.Create(rewritten, block.Statements[j].Origin));
            covered[j] += covered[i];
        }
        if (!ssa.IsStale) {
            ssa.RecomputeLiveness();
        }
        covered[i] = 0;
        return [.. deadReaders];
    }

    /// <summary>Whether <paramref name="statement"/> is a plain register assignment whose defined
    /// versions are all dead - nothing reads the register before the next write: it can go away
    /// with the declaration it reads.</summary>
    private static bool DeadWriter(IrStatement statement, IrSsa ssa) {
        if (statement.Node is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN,
                Left: RegisterNode
            }
            || statement.Effects.MayFault
            || statement.Effects.DeliversInterrupts
            || statement.Effects.TokenWrites != IrTokens.None) {
            return false;
        }
        bool definedRegister = false;
        foreach ((IrSsaValue value, int version) in ssa.Definitions(statement)) {
            if (value >= IrSsaValue.Memory) {
                return false;
            }
            foreach (IrSsaUse use in ssa.LiveUses(version)) {
                if (use.Phi is not null) {
                    return false;
                }
                if (use.ConditionalNewVersion >= 0) {
                    // The version survives only as the not-taken side of a conditional
                    // definition: dead when that definition's statement rewrites every byte
                    // this statement wrote and cannot itself fault away.
                    if (use.Statement is not { } overwriter
                        || overwriter.Effects.MayFault
                        || (statement.Effects.MustWrite & ~overwriter.Effects.MustWrite)
                            != CpuValues.None) {
                        return false;
                    }
                    continue;
                }
                return false;
            }
            definedRegister = true;
        }
        return definedRegister;
    }

    /// <summary>Replaces the one <see cref="VariableReferenceNode"/> named <paramref name="name"/>
    /// inside <paramref name="node"/> with <paramref name="value"/>; null when the reference sits
    /// inside a node shape the substitution cannot reach (same shapes
    /// <see cref="IrPlainTemporaryPropagation"/> refuses) or inside a resolved instruction
    /// field.</summary>
    private static IVisitableAstNode? SubstituteVariable(IVisitableAstNode node, string name,
        ValueNode value) {
        switch (node) {
            case VariableReferenceNode reference:
                return reference.VariableName == name ? value : reference;
            case InstructionFieldNode field:
                return ContainsVariable(field.ResolvedNode, name) ? null : field;
        }
        if (!ContainsVariable(node, name)) {
            return node;
        }
        return node switch {
            ExplicitCastNode or SegmentedAddressNode or OpaqueNode or DivisionNode
                or MultiplicationNode or TupleSwapNode or FailAsUntestedNode or BlockNode
                or IfElseNode or WhileNode or WhileWithExitNode or InstructionPointerNode
                or LoadOffsetParameterNode or SegmentVariableNode or OffsetLiteralNode
                or SignatureFieldNode or MethodGroupNode or SelectorNode or SetInterruptShadowNode
                or ThrowNode or CyclicJumpNode or LocalDeclarationNode or InterruptReturnNode
                or PreTestLoopNode or DoWhileNode or InfiniteLoopNode or SwitchNode
                or CpuFaultTryCatchNode or InstructionBoundaryNode or InstructionNode
                or CfgInstructionNode or RegisterLocalNode or RegisterWriteBackNode => null,
            _ => IrAstChildren.TryMapChildren(node, child => SubstituteVariable(child, name, value))
        };
    }

    private static bool ContainsVariable(IVisitableAstNode node, string name) =>
        CountVariableReferences(node, name) != 0;

    private static int CountVariableReferences(IVisitableAstNode node, string name) =>
        IrAstChildren.Descendants(node).Count(descendant =>
            descendant is VariableReferenceNode reference && reference.VariableName == name);

    /// <summary>A read of a multi-byte register whose reaching version is a chain of partial
    /// definitions - byte halves written by plain assignments since the last full definition, in
    /// either order - prints as the composed value: <c>AL = UInt8[word3, i]; AH = 0; SI = AX;</c>
    /// becomes <c>SI = (ushort)(UInt8[word3, i] | 0 &lt;&lt; 8);</c>, which constant folding reduces
    /// to the zero-extension. Every partial writer's version must be used only by the chain or the
    /// reader, so the writers go away with the read; any other use - the wide register or one byte
    /// read elsewhere - refuses. The legality is the merge's own: SameVersions and MemoryCrossable
    /// for every moved expression, no fault, guard or entry point between the earliest writer and
    /// the reader.</summary>
    private static List<int>? TryComposeHalves(IrBlock block, int j, HashSet<ICfgNode> entryNodes,
        IrSsa ssa, IrEditor editor, int[] covered, IrMemoryAliasAnalysis alias) {
        IrStatement reader = block.Statements[j];
        if (entryNodes.Contains(reader.Origin)
            || reader.Node is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN,
                Left: ValueNode,
                Right: RegisterNode readRegister
            } assign) {
            return null;
        }
        if (readRegister is SegmentRegisterNode) {
            return null;
        }
        CpuValues readMask = IrValueSets.Of(readRegister);
        if ((readMask & CpuValues.Sp) != CpuValues.None
            || (IrEffectsVisitor.Of(assign.Left, false).Reads & readMask) != CpuValues.None
            || (IrEffectsVisitor.Of(assign.Left, false).MayWrite & readMask) != CpuValues.None) {
            return null;
        }
        uint readBits = IrSsaValues.ValueBits(readMask);
        if (readBits == 0 || (readBits & (readBits - 1)) != 0
            || (readMask & (readMask - 1)) == 0) {
            // A single-byte read has nothing to compose.
            return null;
        }
        IrSsaValue value = (IrSsaValue)System.Numerics.BitOperations.TrailingZeroCount(readBits);
        int version = ssa.VersionUsed(reader, value);
        if (version < 0 || ssa.Info(version).Kind != IrSsaDefinitionKind.PartialDefinition) {
            return null;
        }
        // Walk the partial-definition chain back until every read byte is written.
        List<(int Index, IrStatement Statement, CpuValues Mask, ValueNode Expression, int Version)>
            writers = [];
        CpuValues collected = CpuValues.None;
        int cursor = version;
        while ((readMask & ~collected) != CpuValues.None) {
            IrSsaVersionInfo info = ssa.Info(cursor);
            if (info.Kind != IrSsaDefinitionKind.PartialDefinition
                || ssa.DefiningStatement(cursor) is not { } writer
                || !block.Statements.Contains(writer)
                || entryNodes.Contains(writer.Origin)
                || writer.Node is not BinaryOperationNode {
                    BinaryOperation: BinaryOperation.ASSIGN,
                    Left: RegisterNode writtenRegister,
                    Right: ValueNode writtenExpression
                }
                || !Mergeable(writtenExpression)) {
                return null;
            }
            CpuValues writtenMask = IrValueSets.Of(writtenRegister);
            if ((writtenMask & readMask) == CpuValues.None
                || (writtenMask & collected) != CpuValues.None) {
                return null;
            }
            // The writer must define only the byte chain of this register's value.
            if (ssa.Definitions(writer).Any(definition =>
                    definition.Value != value || definition.Version != cursor)) {
                return null;
            }
            writers.Add((IrSsa.PositionIn(block, writer), writer, writtenMask & readMask,
                writtenExpression, cursor));
            collected |= writtenMask & readMask;
            cursor = info.Previous;
        }
        HashSet<IrStatement> chainSites = [.. writers.Select(writer => writer.Statement), reader];
        foreach ((_, IrStatement writer, _, _, int writerVersion) in writers) {
            if (ssa.LiveUses(writerVersion).Any(use => use.Statement is null
                    || !chainSites.Contains(use.Statement))) {
                return null;
            }
        }
        int first = writers.Min(writer => writer.Index);
        for (int k = first; k <= j; k++) {
            IrStatement between = block.Statements[k];
            if (between.Effects.MayFault
                || between.Node is SpeculativeGuardNode) {
                return null;
            }
        }
        foreach ((int index, _, _, ValueNode expression, _) in writers) {
            IrEffects moved = IrEffectsVisitor.Of(expression, false);
            IrTokens tokens = moved.TokenReads & ~IrTokens.Memory;
            if (moved.TouchesMemory) {
                tokens |= IrTokens.Io;
            }
            if (!ssa.SameVersions(moved.Reads, tokens, (block, index + 1), (block, j))
                || !MemoryCrossable(block, index, j, moved, ssa, alias)) {
                return null;
            }
        }
        ValueNode composed = Compose(writers, readRegister.DataType);
        ValueNode folded = (ValueNode)IrConstantFolder.FoldConstantTerms(composed);
        folded = Unwiden(folded);
        if (CountNodes(folded) > MaxMergedNodes
            || (writers.Sum(writer => covered[writer.Index]) + covered[j] > MaxChainLength
                && CountNodes(folded) >= CountNodes(composed))) {
            return null;
        }
        editor.Replace(block, j,
            IrStatement.Create(assign with { Right = folded }, reader.Origin));
        foreach ((int index, _, _, _, _) in writers) {
            covered[j] += covered[index];
            covered[index] = 0;
        }
        if (!ssa.IsStale) {
            ssa.RecomputeLiveness();
        }
        return [.. writers.Select(writer => writer.Index)];
    }

    /// <summary><c>(ushort)(lo | hi &lt;&lt; 8)</c> over the partial definitions' expressions,
    /// each shifted to its byte offset inside the read register.</summary>
    private static ValueNode Compose(
        List<(int Index, IrStatement Statement, CpuValues Mask, ValueNode Expression, int Version)>
            writers, DataType dataType) {
        ValueNode? result = null;
        foreach ((_, _, CpuValues mask, ValueNode expression, _) in writers
                 .OrderBy(writer => System.Numerics.BitOperations.TrailingZeroCount((ulong)writer.Mask))) {
            int shift = (System.Numerics.BitOperations.TrailingZeroCount((ulong)mask) % 4) * 8;
            if (ConstantEvaluation.Resolved(Unwiden(expression)) is { Value: 0 }) {
                continue;
            }
            int width = System.Numerics.BitOperations.PopCount(
                (ulong)mask >> System.Numerics.BitOperations.TrailingZeroCount((ulong)mask)) / 8;
            // Convert to the width the writer covers, not the read's: a (ushort) around a byte
            // arithmetic right side would lose the byte truncation its value semantics require.
            DataType termType = width switch { 1 => DataType.UINT8, 2 => DataType.UINT16,
                _ => DataType.UINT32 };
            ValueNode operand = expression;
            while (operand is TypeConversionNode { Value: { } inner }
                   && inner.DataType.BitWidth <= operand.DataType.BitWidth) {
                operand = inner;
            }
            ValueNode term = operand.DataType == termType
                ? operand
                : new TypeConversionNode(termType, operand);
            if (shift != 0) {
                term = new BinaryOperationNode(dataType, term, BinaryOperation.LEFT_SHIFT,
                    new ConstantNode(DataType.INT32, (ulong)shift));
            }
            result = result is null
                ? term
                : new BinaryOperationNode(dataType, result, BinaryOperation.BITWISE_OR, term);
        }
        result ??= new ConstantNode(dataType, 0);
        return result.DataType.BitWidth <= dataType.BitWidth
            ? result
            : new TypeConversionNode(dataType, result);
    }

    /// <summary><paramref name="node"/> without outer conversions that only widen an already
    /// narrower value: the byte writers' expressions arrive already extended
    /// (<c>(uint)read</c>), which would print as nested casts under the composed conversion.</summary>
    private static ValueNode Unwiden(ValueNode node) {
        while (node is TypeConversionNode { Value: { } inner }
               && Unwiden(inner) is { } candidate
               && candidate.DataType.BitWidth <= node.DataType.BitWidth) {
            node = candidate;
        }
        return node;
    }

    /// <summary>Whether the moved expression's memory reads can cross the statements between the
    /// writer at <paramref name="i"/> and the reader at <paramref name="j"/>: a read not proved
    /// conventional RAM keeps its order with other such reads
    /// (<see cref="IrMemoryAliasAnalysis.DeviceReadOrderSafe"/>); beyond that, yes when the memory
    /// version is unchanged, otherwise every memory-writing statement strictly between them must
    /// be a plain store or a push whose writes are all proved
    /// <see cref="IrAliasRelation.Disjoint"/> from each moved read, all known RAM.</summary>
    private static bool MemoryCrossable(IrBlock block, int i, int j, IrEffects moved, IrSsa ssa,
        IrMemoryAliasAnalysis alias) {
        if ((moved.TokenReads & IrTokens.Memory) == IrTokens.None) {
            return true;
        }
        IReadOnlyList<IrMemoryAccess> reads = alias.Reads(block, i);
        if (!alias.DeviceReadOrderSafe(block, i, j, reads)) {
            return false;
        }
        if (ssa.SameVersions(CpuValues.None, IrTokens.Memory, (block, i + 1), (block, j))) {
            return true;
        }
        for (int k = i + 1; k < j; k++) {
            if ((block.Statements[k].Effects.TokenWrites & IrTokens.Memory) == IrTokens.None) {
                continue;
            }
            if (alias.IsFullMemoryBoundary(block, k)) {
                return false;
            }
            foreach (IrMemoryAccess write in alias.Writes(block, k)) {
                foreach (IrMemoryAccess read in reads) {
                    if (!alias.AllowsRam(write) || !alias.AllowsRam(read)
                        || alias.Relation(write, read) != IrAliasRelation.Disjoint) {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    /// <summary>The index of the one statement of <paramref name="block"/> using
    /// <paramref name="version"/>, located through the site table so edits of earlier passes cannot
    /// stale it; -1 when it is not exactly one statement after <paramref name="writerIndex"/>.</summary>
    private static int ReaderIndex(IrBlock block, IrSsaValue value, int version, int writerIndex,
        IrSsa ssa) {
        int found = -1;
        for (int index = writerIndex + 1; index < block.Statements.Count; index++) {
            if (ssa.VersionUsed(block.Statements[index], value) != version) {
                continue;
            }
            if (found >= 0) {
                return -1;
            }
            found = index;
        }
        return found;
    }

    private static bool Overlaps(RegisterNode register, RegisterNode other) =>
        (IrValueSets.Of(register) & IrValueSets.Of(other)) != CpuValues.None;

    /// <summary>Runs only the temporary-declaration merge (the last step of <see cref="Apply"/>)
    /// over every block of <paramref name="method"/>: for a pass that creates wide temporaries after
    /// this pass's own run (<see cref="IrDoublePrecision"/>).</summary>
    internal static void ApplyTemporaries(IrMethod method, IrEditor editor, GenerationPolicy policy) {
        HashSet<ICfgNode> entryNodes = method.Entries.Select(entry => entry.Node).ToHashSet();
        IrTemporaries temporaries = IrTemporaries.Build(method);
        foreach (IrBlock block in method.Blocks) {
            IrSsa ssa = editor.Ssa;
            if (ssa.IsStale) {
                ssa = editor.Rebind(method, "ssa rebind expression propagation");
            }
            MergeTemporariesBlock(block, method, entryNodes, ssa, editor,
                new IrMemoryAliasAnalysis(method, ssa, policy), temporaries, policy);
        }
    }

    private static void MergeTemporariesBlock(IrBlock block, IrMethod method,
        HashSet<ICfgNode> entryNodes, IrSsa ssa, IrEditor editor, IrMemoryAliasAnalysis alias,
        IrTemporaries temporaries, GenerationPolicy policy) {
        if (ssa.IsLivenessStale) {
            ssa.RecomputeLiveness();
        }
        int[] covered = new int[block.Statements.Count];
        Array.Fill(covered, 1);
        List<int> removed = [];
        for (int i = 0; i < block.Statements.Count; i++) {
            if (ssa.IsStale) {
                ssa = editor.Rebind(method, "ssa rebind expression propagation");
                alias = new IrMemoryAliasAnalysis(method, ssa, policy);
                temporaries = IrTemporaries.Build(method);
            }
            if (covered[i] > 0
                && TryMergeTemporary(block, method, i, entryNodes, ssa, editor, covered,
                    alias, temporaries) is { } tempRemoved) {
                removed.Add(i);
                removed.AddRange(tempRemoved);
            }
        }
        foreach (int index in removed.OrderByDescending(index => index)) {
            editor.Remove(block, index);
        }
    }

    /// <summary>Whether <paramref name="node"/> can travel into another statement: a pure read of
    /// registers, constants, conversions, unary and non-assignment binary operations, or a memory
    /// read whose address parts are all mergeable.</summary>
    private static bool Mergeable(ValueNode node) =>
        IrAstChildren.Descendants(node).All(descendant => descendant switch {
            RegisterNode or ConstantNode or InstructionFieldNode or TypeConversionNode
                or UnaryOperationNode or ConditionalValueNode or SegmentedPointerNode
                or AbsolutePointerNode => true,
            ExplicitCastNode cast => cast.Value is ValueNode,
            BinaryOperationNode binary => binary.BinaryOperation != BinaryOperation.ASSIGN,
            _ => false
        });

    /// <summary>A merge target: a register, a segment register or a memory pointer whose address is mergeable.</summary>
    private static bool MergeableTarget(ValueNode node) => node switch {
        RegisterNode => true,
        SegmentedPointerNode pointer => Mergeable(pointer.Segment) && Mergeable(pointer.Offset)
            && (pointer.DefaultSegment is null || Mergeable(pointer.DefaultSegment)),
        AbsolutePointerNode pointer => Mergeable(pointer.AbsoluteAddress),
        _ => false
    };

    /// <summary>The registers a write target reads: only the address parts of a memory pointer; a
    /// register target is written, not read.</summary>
    private static void CollectAddressRegisters(ValueNode target, List<RegisterNode> registers) {
        switch (target) {
            case SegmentedPointerNode pointer:
                CollectRegisters(pointer.Segment, registers);
                if (pointer.DefaultSegment is not null) {
                    CollectRegisters(pointer.DefaultSegment, registers);
                }
                CollectRegisters(pointer.Offset, registers);
                return;
            case AbsolutePointerNode pointer:
                CollectRegisters(pointer.AbsoluteAddress, registers);
                return;
        }
    }

    private static void CollectRegisters(ValueNode node, List<RegisterNode> registers) =>
        registers.AddRange(IrAstChildren.Descendants(node).OfType<RegisterNode>()
            .Where(register => register is not SegmentRegisterNode));

    internal static ValueNode Substitute(ValueNode node, RegisterNode target, ValueNode replacement) =>
        node switch {
            RegisterNode register when register.Equals(target) => replacement,
            _ => (ValueNode)IrAstChildren.MapChildren(node,
                child => child is ValueNode value ? Substitute(value, target, replacement) : child)
        };

    private static int CountNodes(ValueNode node) {
        return node switch {
            BinaryOperationNode binary => 1 + CountNodes(binary.Left) + CountNodes(binary.Right),
            UnaryOperationNode unary => 1 + CountNodes(unary.Value),
            TypeConversionNode conversion => 1 + CountNodes(conversion.Value),
            ExplicitCastNode { Value: ValueNode inner } => 1 + CountNodes(inner),
            ConditionalValueNode conditional => 1 + CountNodes(conditional.Condition)
                + CountNodes(conditional.WhenTrue) + CountNodes(conditional.WhenFalse),
            SegmentedPointerNode pointer => 1 + CountNodes(pointer.Segment) + CountNodes(pointer.Offset)
                + (pointer.DefaultSegment is null ? 0 : CountNodes(pointer.DefaultSegment)),
            AbsolutePointerNode pointer => 1 + CountNodes(pointer.AbsoluteAddress),
            MethodCallValueNode call => 1 + call.CallNode.Arguments
                .Sum(argument => argument is ValueNode value ? CountNodes(value) : 1),
            _ => 1
        };
    }
}
