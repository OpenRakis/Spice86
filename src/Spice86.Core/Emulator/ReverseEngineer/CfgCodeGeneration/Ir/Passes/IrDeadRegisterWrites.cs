namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using System.Linq;

/// <summary>
/// The dead register write removal: a statement that is just a write to a general or
/// segment register (no memory, call or variable on the right side, not SP/ESP) is removed when every
/// register version it defines has no live use. A use that only carries the bytes a later partial
/// definition overwrites anyway does not keep the write alive. Entry statements and statements with
/// unknown-like effects are never touched.
/// </summary>
internal static class IrDeadRegisterWrites {
    /// <summary>Removes the dead plain register writes of <paramref name="method"/>.</summary>
    public static void Apply(IrMethod method, IrSsa ssa) {
        HashSet<ICfgNode> entryNodes = method.Entries.Select(entry => entry.Node).ToHashSet();
        Dictionary<ICfgNode, List<IrOrigins.Located>> byOrigin = IrOrigins.ByOrigin(method);
        HashSet<IrBlock> observed = ObservingBlocks(method);
        List<(IrBlock Block, int Index)> removals = [];
        foreach (IrBlock block in method.Blocks) {
            if (!observed.Contains(block)) {
                continue;
            }
            for (int index = 0; index < block.Statements.Count; index++) {
                IrStatement statement = block.Statements[index];
                if (Drops(statement, entryNodes, ssa)
                    && OriginIsJustTheWrite(byOrigin[statement.Origin], statement)) {
                    removals.Add((block, index));
                }
            }
        }
        foreach ((IrBlock block, int index) in removals.OrderByDescending(removal => removal.Index)) {
            ssa.Remove(block.Statements[index]);
            block.Statements.RemoveAt(index);
        }
    }

    /// <summary>The blocks that can reach an exit observing machine state: every transfer except a
    /// <see cref="FailAsUntestedNode"/>, which aborts the program with nothing observed. Writes in
    /// pure-abort regions keep their boundary-everything shape instead of being stripped wholesale.</summary>
    private static HashSet<IrBlock> ObservingBlocks(IrMethod method) {
        HashSet<IrBlock> observing = [];
        Queue<IrBlock> worklist = [];
        foreach (IrBlock block in method.Blocks) {
            if (block.Terminator is not IrExit { Transfer: not FailAsUntestedNode }
                || !observing.Add(block)) {
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
            || (register is not SegmentRegisterNode
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
        IrValues mask = IrSsaValues.MaskOf(value);
        if ((partial & (1u << (int)value)) == 0
            || (user.Effects.Reads & mask) != IrValues.None) {
            return false;
        }
        int userVersion = use.ConditionalNewVersion;
        if (ssa.Info(userVersion).Previous != version) {
            return false;
        }
        IrValues written = (statement.Effects.MustWrite | statement.Effects.MayWrite) & mask;
        IrValues overwritten = user.Effects.MustWrite & mask;
        return (written & ~overwritten) == IrValues.None;
    }

    private static bool IsPlainValue(ValueNode value) =>
        IrAstChildren.Descendants(value).All(descendant => descendant switch {
            RegisterNode or SegmentRegisterNode or ConstantNode or TypeConversionNode
                or UnaryOperationNode or InstructionFieldNode => true,
            BinaryOperationNode binary => binary.BinaryOperation != BinaryOperation.ASSIGN,
            _ => false
        });
}
