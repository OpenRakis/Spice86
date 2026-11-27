namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using System.Linq;

/// <summary>
/// The dead register write removal: a statement that is just a write to a general or
/// segment register (no memory, call or variable on the right side, not SP/ESP) is removed when every
/// register version it defines has no live use. A use that only carries the bytes a later partial
/// definition overwrites anyway does not keep the write alive. Entry statements and statements with
/// unknown-like effects are never touched.
/// A pop into a dead general register keeps its stack read, its access check and its SP step: only
/// the dead register write goes, replaced by the bare pop statement.
/// </summary>
internal static class IrDeadRegisterWrites {
    /// <summary>Removes the dead plain register writes of <paramref name="method"/>.</summary>
    public static void Apply(IrMethod method, IrEditor editor) {
        IrSsa ssa = editor.Ssa;
        HashSet<ICfgNode> entryNodes = method.Entries.Select(entry => entry.Node).ToHashSet();
        Dictionary<ICfgNode, List<IrOrigins.Located>> byOrigin = IrOrigins.ByOrigin(method);
        HashSet<IrBlock> observed = ObservingBlocks(method);
        HashSet<IrStatement> frameStatements = FrameStatements(method, ssa);
        List<(IrBlock Block, int Index, IrStatement? Replacement)> edits = [];
        foreach (IrBlock block in method.Blocks) {
            if (!observed.Contains(block)) {
                continue;
            }
            for (int index = 0; index < block.Statements.Count; index++) {
                IrStatement statement = block.Statements[index];
                if (frameStatements.Contains(statement)) {
                    continue;
                }
                if (Drops(statement, entryNodes, ssa)
                    && OriginIsJustTheWrite(byOrigin[statement.Origin], statement)) {
                    edits.Add((block, index, null));
                    continue;
                }
                if (DeadPopTarget(statement, entryNodes, ssa) is { } barePop) {
                    edits.Add((block, index,
                        IrStatement.Create(barePop, statement.Origin)));
                }
            }
        }
        foreach ((IrBlock block, int index, IrStatement? replacement)
            in edits.OrderByDescending(edit => edit.Index)) {
            if (replacement is null) {
                editor.Remove(block, index);
                continue;
            }
            editor.Replace(block, index, replacement);
        }
    }

    /// <summary>The statements the frame recognition claims: the <c>mov bp,sp</c> of each
    /// recognized frame and the <c>pop bp</c> of each pop epilogue. Their register writes stay so
    /// <see cref="Memory.IrFrames"/> sees the same prologue and epilogue at emission; without them
    /// no frame is recognized, the frame's arguments stop printing and the epilogue prints a bare
    /// pop or a release.</summary>
    private static HashSet<IrStatement> FrameStatements(IrMethod method, IrSsa ssa) {
        HashSet<IrStatement> statements = [];
        if (!method.Blocks.SelectMany(block => block.Statements).Any(IrFrames.IsPushBp)) {
            return statements;
        }
        IrMemoryAliasAnalysis alias = new(method, ssa);
        IrFrames frames = IrFrames.Compute(method, ssa,
            IrStackSlots.Compute(method, alias, _ => null), _ => CpuValues.Registers);
        foreach (IrEntry entry in method.Entries) {
            if (frames.FrameAt(entry) is not { } frame) {
                continue;
            }
            statements.Add(frame.SetBp);
            statements.UnionWith(frame.Epilogues.Select(epilogue => epilogue.Restore)
                .OfType<IrStatement>());
        }
        return statements;
    }

    /// <summary>The bare pop a dead register pop becomes: a statement
    /// <c>R = Stack.Pop16()</c> / <c>Pop32()</c> whose target is a general register other than SP/ESP
    /// and whose every defined version of that register has no live use, or <see langword="null"/>
    /// when the pop's target is still observed. The stack read, the access check and the SP step
    /// stay; only the dead register write goes.</summary>
    private static StackCallNode? DeadPopTarget(IrStatement statement, HashSet<ICfgNode> entryNodes,
        IrSsa ssa) {
        if (entryNodes.Contains(statement.Origin)
            || statement.Node is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN,
                Left: RegisterNode register,
                Right: StackCallValueNode { Operation: StackOperation.Pop } pop }) {
            return null;
        }
        if (IsStackPointerOrNotGeneral(register)) {
            return null;
        }
        CpuValues target = IrValueSets.Of(register);
        foreach ((IrSsaValue value, int version) in ssa.Definitions(statement)) {
            if ((IrSsaValues.MaskOf(value) & target) == CpuValues.None) {
                continue;
            }
            if (value > IrSsaValue.Gs || DefinedVersionsAreUsed(ssa, statement, value, version)) {
                return null;
            }
        }
        return new StackCallNode(StackOperation.Pop, pop.Width);
    }

    private static bool IsStackPointerOrNotGeneral(RegisterNode register) =>
        register is SegmentRegisterNode
        // UINT8 index 4 is AH, not SP; only word/dword index-4 stores are stack writes.
        || (register.DataType != DataType.UINT8
            && register.RegisterIndex == (int)RegisterIndex.SpIndex);

    /// <summary>The blocks that can reach an exit observing machine state or a
    /// statement or terminator that may fault. A fault observes the CPU state even
    /// when the normal path ends in <see cref="FailAsUntestedNode"/>. Regions with
    /// only untested aborts and no fault path keep their writes.</summary>
    private static HashSet<IrBlock> ObservingBlocks(IrMethod method) {
        HashSet<IrBlock> observing = [];
        Queue<IrBlock> worklist = [];
        foreach (IrBlock block in method.Blocks) {
            bool observesState =
                block.Terminator is IrExit { Transfer: not FailAsUntestedNode }
                || block.Terminator.Effects.MayFault
                || block.Statements.Any(statement => statement.Effects.MayFault);
            if (!observesState || !observing.Add(block)) {
                continue;
            }
            worklist.Enqueue(block);
        }
        Dictionary<IrBlock, List<IrBlock>> predecessors = [];
        foreach (IrBlock block in method.Blocks) {
            foreach (IrEdge edge in block.Terminator.Edges) {
                if (!predecessors.TryGetValue(edge.Target, out List<IrBlock>? incoming)) {
                    predecessors[edge.Target] = incoming = [];
                }
                incoming.Add(block);
            }
        }
        while (worklist.Count > 0) {
            IrBlock block = worklist.Dequeue();
            if (!predecessors.TryGetValue(block, out List<IrBlock>? incoming)) {
                continue;
            }
            foreach (IrBlock other in incoming) {
                if (!observing.Add(other)) {
                    continue;
                }
                worklist.Enqueue(other);
            }
        }
        return observing;
    }

    /// <summary>The candidate is the only substantive statement of its instruction - every sibling is
    /// boundary trivia (the start marker, the source comment, the event check, a speculative guard).
    /// A write inside an instruction that does more (a <c>movs</c>'s direction step, an
    /// <c>xchg</c>'s second half) is never dropped on its own.</summary>
    private static bool OriginIsJustTheWrite(IReadOnlyList<IrOrigins.Located> statements,
        IrStatement candidate) =>
        statements.All(located => ReferenceEquals(located.Statement, candidate)
            || located.Statement.Node is InstructionStartNode or CommentNode
                or CheckExternalEventsNode or SpeculativeGuardNode);

    private static bool Drops(IrStatement statement, HashSet<ICfgNode> entryNodes, IrSsa ssa) {
        if (entryNodes.Contains(statement.Origin)
            || statement.Effects.TokenReads != IrTokens.None
            || statement.Effects.TokenWrites != IrTokens.None
            || statement.Node is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN,
                Left: RegisterNode register } assign
            // UINT8 index 4 is AH, not SP; only word/dword index-4 stores are stack writes.
            || (register is not SegmentRegisterNode
                && register.DataType != DataType.UINT8
                && register.RegisterIndex == (int)RegisterIndex.SpIndex)
            || !IsPlainValue(assign.Right)) {
            return false;
        }
        foreach ((IrSsaValue value, int version) in ssa.Definitions(statement)) {
            if (value > IrSsaValue.Gs || DefinedVersionsAreUsed(ssa, statement, value, version)) {
                return false;
            }
        }
        return true;
    }

    private static bool DefinedVersionsAreUsed(IrSsa ssa, IrStatement statement,
        IrSsaValue value, int version) {
        foreach (IrSsaUse use in ssa.LiveUses(version)) {
            if (!InheritsAcrossAnOverwrite(ssa, statement, value, version, use)) {
                return true;
            }
        }
        return false;
    }

    /// <summary>Whether <paramref name="use"/> only carries the bytes of <paramref name="version"/> its
    /// definition did not write: the use is a partial definition's previous-version dependency, the
    /// user's statement overwrites every byte <paramref name="statement"/>
    /// may write of <paramref name="value"/>, so the part <paramref name="version"/> contributed is
    /// overwritten and nothing observes it.</summary>
    private static bool InheritsAcrossAnOverwrite(IrSsa ssa, IrStatement statement,
        IrSsaValue value, int version, IrSsaUse use) {
        if (use.ConditionalNewVersion < 0 || use.Statement is not { } user) {
            return false;
        }
        (uint _, uint partial) = IrSsaValues.WriteBits(user.Effects);
        CpuValues mask = IrSsaValues.MaskOf(value);
        if ((partial & (1u << (int)value)) == 0
            || (user.Effects.Reads & mask) != CpuValues.None) {
            return false;
        }
        int userVersion = use.ConditionalNewVersion;
        if (ssa.Info(userVersion).Previous != version) {
            return false;
        }
        CpuValues written = (statement.Effects.MustWrite | statement.Effects.MayWrite) & mask;
        CpuValues overwritten = user.Effects.MustWrite & mask;
        return (written & ~overwritten) == CpuValues.None;
    }

    private static bool IsPlainValue(ValueNode value) =>
        IrAstChildren.Descendants(value).All(descendant => descendant switch {
            RegisterNode or SegmentRegisterNode or ConstantNode or TypeConversionNode
                or UnaryOperationNode or InstructionFieldNode => true,
            BinaryOperationNode binary => binary.BinaryOperation != BinaryOperation.ASSIGN,
            _ => false
        });
}
