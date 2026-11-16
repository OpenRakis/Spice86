namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// The saved-register merge, a printing-only rewrite applied at the end of
/// <see cref="IrMethodEmitter.Analyze"/> on the final body. It merges, per block, the runs the
/// pushes and pops of a register save form:
/// <list type="bullet">
/// <item>the method's entry run: two or more consecutive 16-bit pushes of a general register other
/// than SP (or of a register local of the register-local plan), preceded only by local declarations
/// and event checks, become one <see cref="StackSaveNode"/> (<c>Stack.Save(BX, CX, DX);</c>);</item>
/// <item>two to six consecutive pops into distinct general registers other than SP (or register
/// locals) followed only by register write-backs and a <c>return</c> in the same block become one
/// <see cref="StackRestoreNode"/> (<c>(DX, CX, BX) = Stack.Restore3();</c>);</item>
/// <item>consecutive bare 16-bit pops (the register writes
/// <see cref="Passes.IrDeadRegisterWrites"/> proved dead) become one
/// <see cref="StackReleaseNode"/> (<c>Stack.Release(2);</c>).</item>
/// </list>
/// The merged calls run the same <c>Stack.Push16</c> / <c>Stack.Pop16</c> calls in the same order, so
/// the stack writes, reads, access checks and SP steps are unchanged. A save evaluates every value
/// before the first push: exact because each value is a register or local read (no token read, no
/// call) and not SP, so no earlier push changes a value a later argument reads. A restore assigns
/// its targets after the last pop: exact because a pop reads only SS and SP, the targets are
/// distinct general registers (not SP) or locals, and a run inside a fault-observed region is never
/// merged (the pass does not enter <see cref="CpuFaultTryCatchNode"/> bodies), so nothing observes
/// the state between two pops. A statement that prints anything else between two candidates ends
/// the run; a boundary that prints nothing does not.
/// </summary>
internal static class StackRuns {
    /// <summary>Merges the stack runs of <paramref name="body"/>.</summary>
    public static BlockNode Apply(BlockNode body) => ProcessBlock(RewriteChildren(body), true);

    private static IVisitableAstNode Rewrite(IVisitableAstNode node) {
        // A run inside a fault-observed region is never merged: a fault observes the CPU state
        // between two pushes or two pops.
        if (node is CpuFaultTryCatchNode) {
            return node;
        }
        if (node is BlockNode block) {
            return ProcessBlock(RewriteChildren(block), false);
        }
        return IrAstChildren.MapChildren(node, Rewrite);
    }

    private static BlockNode RewriteChildren(BlockNode block) =>
        new(block.Statements.Select(Rewrite).ToArray());

    /// <summary>The statements of one block as <c>(child index, inner index)</c> entries: a
    /// statement inside an instruction boundary is an entry of that boundary's body, a direct
    /// child an entry of inner index -1. Runs merge across boundaries that print nothing.</summary>
    private static List<Entry> EntriesOf(IReadOnlyList<IVisitableAstNode> children) {
        List<Entry> entries = [];
        for (int child = 0; child < children.Count; child++) {
            if (children[child] is InstructionBoundaryNode boundary) {
                for (int inner = 0; inner < boundary.Body.Statements.Count; inner++) {
                    entries.Add(new(child, inner, boundary.Body.Statements[inner]));
                }
                continue;
            }
            entries.Add(new(child, -1, children[child]));
        }
        return entries;
    }

    private static BlockNode ProcessBlock(BlockNode block, bool topLevel) {
        List<IVisitableAstNode> children = [.. block.Statements];
        List<Entry> entries = EntriesOf(children);
        List<Edit> edits = MergesOf(entries, children, topLevel);
        if (edits.Count == 0) {
            return block;
        }
        List<IVisitableAstNode> rewritten = [];
        for (int child = 0; child < children.Count; child++) {
            IVisitableAstNode node = children[child];
            if (node is not InstructionBoundaryNode boundary) {
                Edit? edit = edits.FirstOrDefault(e => e.Child == child && e.Inner == -1);
                if (edit?.Replacement is { } replacement) {
                    rewritten.Add(replacement);
                } else if (edit is null) {
                    rewritten.Add(node);
                }
                continue;
            }
            List<IVisitableAstNode> inner = [];
            for (int index = 0; index < boundary.Body.Statements.Count; index++) {
                Edit? edit = edits.FirstOrDefault(e => e.Child == child && e.Inner == index);
                if (edit?.Replacement is { } replacement) {
                    inner.Add(replacement);
                } else if (edit is null) {
                    inner.Add(boundary.Body.Statements[index]);
                }
            }
            // A boundary left with only trivia keeps its anchor and its comment; an empty
            // unanchored one goes.
            if (inner.Count == 0 && boundary.Anchor is null) {
                continue;
            }
            rewritten.Add(boundary with { Body = new BlockNode([.. inner]) });
        }
        return new BlockNode([.. rewritten]);
    }

    private static List<Edit> MergesOf(List<Entry> entries, List<IVisitableAstNode> children,
        bool topLevel) {
        List<Edit> edits = [];
        int index = 0;
        while (index < entries.Count) {
            Kind kind = KindOf(entries[index].Node);
            if (kind == Kind.None) {
                index++;
                continue;
            }
            int last = index;
            while (last + 1 < entries.Count
                && (KindOf(entries[last + 1].Node) == kind
                    || entries[last + 1].Node is InstructionStartNode)) {
                last++;
            }
            int runEnd = last + 1;
            while (entries[last].Node is InstructionStartNode) {
                last--;
            }
            int count = Count(entries, index, last, kind);
            switch (kind) {
                case Kind.Push when topLevel && count >= 2 && EntryRun(entries, index):
                    edits.AddRange(Merge(children, entries, index, last,
                        new StackSaveNode([.. Values(entries, index, last)])));
                    break;
                case Kind.PopAssign:
                case Kind.BarePop:
                case Kind.Release: {
                    int familyEnd = index;
                    while (familyEnd + 1 < entries.Count
                        && (IsPopMember(entries[familyEnd + 1].Node)
                            || entries[familyEnd + 1].Node is InstructionStartNode)) {
                        familyEnd++;
                    }
                    int familyRunEnd = familyEnd + 1;
                    while (entries[familyEnd].Node is InstructionStartNode) {
                        familyEnd--;
                    }
                    int pops = PopCount(entries, index, familyEnd);
                    if (Count(entries, index, familyEnd, Kind.PopAssign) == 0) {
                        edits.AddRange(Merge(children, entries, index, familyEnd,
                            new StackReleaseNode(pops)));
                    } else if (RestoreFollows(entries, familyRunEnd) && pops is >= 2 and <= 6
                        && DistinctTargets(entries, index, familyEnd)) {
                        edits.AddRange(Merge(children, entries, index, familyEnd,
                            new StackRestoreNode([.. Targets(entries, index, familyEnd)])));
                    } else {
                        edits.AddRange(HomogeneousMerges(children, entries, index, familyEnd));
                    }
                    index = familyRunEnd;
                    continue;
                }
            }
            index = runEnd;
        }
        return edits;
    }

    /// <summary>The family-wide merge refused: walk the run again one kind at a time so the bare
    /// pops still release and a pop-assign run merges exactly when the homogeneous rules allow
    /// it.</summary>
    private static List<Edit> HomogeneousMerges(List<IVisitableAstNode> children,
        List<Entry> entries, int start, int end) {
        List<Edit> edits = [];
        int index = start;
        while (index <= end) {
            Kind kind = KindOf(entries[index].Node);
            if (kind == Kind.None) {
                index++;
                continue;
            }
            int last = index;
            while (last + 1 <= end
                && (KindOf(entries[last + 1].Node) == kind
                    || entries[last + 1].Node is InstructionStartNode)) {
                last++;
            }
            int runEnd = last + 1;
            while (entries[last].Node is InstructionStartNode) {
                last--;
            }
            int count = Count(entries, index, last, kind);
            if (kind == Kind.BarePop) {
                edits.AddRange(Merge(children, entries, index, last,
                    new StackReleaseNode(count)));
            } else if (RestoreFollows(entries, runEnd) && count is >= 2 and <= 6
                && DistinctTargets(entries, index, last)) {
                edits.AddRange(Merge(children, entries, index, last,
                    new StackRestoreNode([.. Targets(entries, index, last)])));
            }
            index = runEnd;
        }
        return edits;
    }

    /// <summary>The push run starting at <paramref name="runIndex"/> is the method's entry run when
    /// everything before it in the block is a local declaration, an event check or the instruction
    /// start marker.</summary>
    private static bool EntryRun(List<Entry> entries, int runIndex) {
        for (int index = 0; index < runIndex; index++) {
            if (entries[index].Node is not (InstructionStartNode or LocalDeclarationNode
                    or VariableDeclarationNode or CheckExternalEventsNode)) {
                return false;
            }
        }
        return true;
    }

    /// <summary>A pop-assign run merges only when what follows it in the block is nothing or only
    /// register write-backs, and then a <c>return</c>.</summary>
    private static bool RestoreFollows(List<Entry> entries, int index) {
        while (index < entries.Count
            && entries[index].Node is InstructionStartNode or RegisterWriteBackNode) {
            index++;
        }
        return index == entries.Count
            || entries[index].Node is ReturnNode or InterruptReturnNode;
    }

    private static int Count(List<Entry> entries, int start, int end, Kind kind) {
        int count = 0;
        for (int index = start; index <= end; index++) {
            if (KindOf(entries[index].Node) == kind) {
                count++;
            }
        }
        return count;
    }

    private static IEnumerable<ValueNode> Values(List<Entry> entries, int start, int end) =>
        Candidates(entries, start, end)
            .Select(node => (ValueNode)((StackCallNode)node).Arguments[0]);

    /// <summary>Whether <paramref name="node"/> can join a pop run: a pop-assign, a bare pop, or a
    /// <see cref="StackReleaseNode"/> an inner boundary body's merge already folded dead pops into -
    /// its slots are discard positions of the wider run.</summary>
    private static bool IsPopMember(IVisitableAstNode node) =>
        KindOf(node) is Kind.PopAssign or Kind.BarePop or Kind.Release;

    private static IEnumerable<ValueNode?> Targets(List<Entry> entries, int start, int end) {
        for (int index = start; index <= end; index++) {
            IVisitableAstNode node = entries[index].Node;
            if (node is StackReleaseNode release) {
                for (int slot = 0; slot < release.Count; slot++) {
                    yield return null;
                }
                continue;
            }
            switch (KindOf(node)) {
                case Kind.PopAssign:
                    yield return (ValueNode)((BinaryOperationNode)node).Left;
                    break;
                case Kind.BarePop:
                    yield return null;
                    break;
            }
        }
    }

    private static bool DistinctTargets(List<Entry> entries, int start, int end) {
        List<ValueNode?> live = [.. Targets(entries, start, end)
            .Where(target => target is not null)];
        return live.Distinct().Count() == live.Count;
    }

    private static int PopCount(List<Entry> entries, int start, int end) {
        int count = 0;
        for (int index = start; index <= end; index++) {
            IVisitableAstNode node = entries[index].Node;
            if (node is StackReleaseNode release) {
                count += release.Count;
            } else if (KindOf(node) is Kind.PopAssign or Kind.BarePop) {
                count++;
            }
        }
        return count;
    }

    private static IEnumerable<IVisitableAstNode> Candidates(List<Entry> entries, int start,
        int end) {
        for (int index = start; index <= end; index++) {
            if (KindOf(entries[index].Node) != Kind.None) {
                yield return entries[index].Node;
            }
        }
    }

    /// <summary>The merged node takes the first candidate's slot; the other candidates are removed.
    /// A goto landing between two merged statements would see a state that never existed, so a run
    /// whose span crosses an anchored boundary after the first slot is refused.</summary>
    private static List<Edit> Merge(List<IVisitableAstNode> children, List<Entry> entries,
        int start, int end, IVisitableAstNode merged) {
        List<Edit> edits = [];
        int previousChild = -1;
        bool first = true;
        for (int index = start; index <= end; index++) {
            if (KindOf(entries[index].Node) == Kind.None) {
                continue;
            }
            int child = entries[index].Child;
            if (!first) {
                for (int between = previousChild + 1; between <= child; between++) {
                    if (children[between] is InstructionBoundaryNode { Anchor: not null }) {
                        return [];
                    }
                }
            }
            edits.Add(new(child, entries[index].Inner, first ? merged : null));
            previousChild = child;
            first = false;
        }
        return edits;
    }

    private static Kind KindOf(IVisitableAstNode node) => node switch {
        StackCallNode { Operation: StackOperation.Push, Width: BitWidth.WORD_16, Arguments: [ValueNode value] }
            when IsSaveRegister(value) => Kind.Push,
        BinaryOperationNode {
            BinaryOperation: BinaryOperation.ASSIGN,
            Left: ValueNode target,
            Right: StackCallValueNode { Operation: StackOperation.Pop, Width: BitWidth.WORD_16 } }
            when IsSaveRegister(target) => Kind.PopAssign,
        StackCallNode { Operation: StackOperation.Pop, Width: BitWidth.WORD_16 } => Kind.BarePop,
        StackReleaseNode => Kind.Release,
        _ => Kind.None
    };

    /// <summary>A save value or restore target: a general register other than SP (word/dword index
    /// 4; the byte form is AH) or a register local of one.</summary>
    private static bool IsSaveRegister(ValueNode node) {
        RegisterNode? register = node switch {
            RegisterNode direct => direct,
            RegisterLocalNode local => local.Register as RegisterNode,
            _ => null
        };
        return register is not null
            && register is not SegmentRegisterNode
            && (register.DataType == DataType.UINT8
                || register.RegisterIndex != (int)RegisterIndex.SpIndex);
    }

    private enum Kind {
        None,
        Push,
        PopAssign,
        BarePop,
        Release
    }

    private sealed record Entry(int Child, int Inner, IVisitableAstNode Node);

    private sealed record Edit(int Child, int Inner, IVisitableAstNode? Replacement);
}
