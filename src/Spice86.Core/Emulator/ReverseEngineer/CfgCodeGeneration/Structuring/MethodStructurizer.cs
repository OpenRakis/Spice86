namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using System.Collections.Generic;
using System.Linq;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

/// <summary>
/// Builds the body of a generated method from its lowered blocks. Every transfer of the blocks is still an explicit
/// <c>goto</c>, so the order and nesting chosen here only change readability; <c>FallthroughGotoRemover</c> runs on
/// the result.
/// </summary>
internal static class MethodStructurizer {
    /// <summary>
    /// The method body: the lowered blocks in layout order, every natural loop gathered into a <c>do</c> or
    /// <c>while (true)</c> loop at its header's place, child loops inside their parent's body, and a forward branch whose
    /// arms are entered only from it nested in an <c>if</c> (one arm that comes back to the other target) or an <c>if</c>
    /// / <c>else</c> (two arms that meet at one join), and the arms of a near jump table nested in their <c>case</c> sections.
    /// </summary>
    /// <param name="blocks">The lowered blocks, in layout order.</param>
    /// <param name="loops">The natural loops of the method's block graph.</param>
    /// <param name="entryNodes">The partition entry nodes of the method.</param>
    /// <param name="methodName">The method name, for error messages.</param>
    /// <param name="refusals">Filled with the reason each <c>goto</c> arm of a refused branch or <c>switch</c> section
    /// was not nested, keyed by the <see cref="GotoNode"/> instance (later records overwrite earlier ones).</param>
    /// <param name="placeLoopExits">When set, the exit of a loop that the loop's <c>goto</c>s target the most is moved
    /// right after the loop in the printing order, so the <c>goto</c>s to it become <c>break</c>s.</param>
    /// <exception cref="InvalidOperationException">A natural loop header does not start with <c>CheckExternalEvents</c>.</exception>
    /// <exception cref="InvalidOperationException">A loop header or an arm head is not the first block of its loop or arm in the layout.</exception>
    public static BlockNode Structure(IReadOnlyList<LoweredBlock> blocks, NaturalLoopForest loops, IReadOnlySet<ICfgNode> entryNodes, string methodName,
        Dictionary<GotoNode, GotoKeepReason> refusals, bool placeLoopExits) {
        CheckLoopHeaders(blocks, loops, methodName);
        Dictionary<ICfgNode, int> gotoCountByTarget = blocks.SelectMany(block => Gotos(block.Body))
            .CountBy(jump => jump.Target).ToDictionary(pair => pair.Key, pair => pair.Value);
        return new RegionWalker(loops, entryNodes, gotoCountByTarget, refusals, methodName, placeLoopExits).Walk(blocks, null);
    }

    /// <summary>The number of <see cref="GotoNode"/>s anywhere in <paramref name="node"/>.</summary>
    internal static int TotalGotos(IVisitableAstNode node) => Gotos(node).Count();

    /// <summary>Whether <paramref name="loop"/> can become a structured loop: no partition entry inside it other than its header.</summary>
    internal static bool IsStructurable(NaturalLoop loop, IReadOnlySet<ICfgNode> entryNodes) {
        ICfgNode header = loop.Header.Entry;
        return !loop.Blocks.SelectMany(block => block.Instructions)
            .Any(node => entryNodes.Contains(node) && !ReferenceEquals(node, header));
    }

    private static GotoNode? OnlyGoto(IVisitableAstNode arm) =>
        Sequence(arm).Statements is [GotoNode jump] ? jump : null;

    private static ICfgNode? OnlyGotoTarget(IVisitableAstNode arm) => OnlyGoto(arm)?.Target;

    private static bool IsOnlyGotoTo(IVisitableAstNode arm, ICfgNode target) =>
        Sequence(arm).Statements is [GotoNode jump] && ReferenceEquals(jump.Target, target);

    /// <summary>
    /// The statement after a <c>do</c> loop, when the exit arm is a single <c>goto</c> that does not restart the loop,
    /// or a <c>FailAsUntested</c> whose message reads no value, so it can move after the loop.
    /// </summary>
    private static IVisitableAstNode? ExitStatement(IVisitableAstNode arm, ICfgNode header) {
        if (Sequence(arm).Statements is not [IVisitableAstNode single]) {
            return null;
        }
        if (single is GotoNode jump && !ReferenceEquals(jump.Target, header)) {
            return jump;
        }
        if (single is FailAsUntestedNode failure && failure.Parts.All(part => part.Value is null)) {
            return failure;
        }
        return null;
    }

    /// <summary>
    /// When the last statement of <paramref name="block"/> is an instruction boundary that ends with a <typeparamref name="T"/>:
    /// that final statement, and the statements before it (the block's other statements, then the boundary without it).
    /// </summary>
    private static (List<IVisitableAstNode> Head, T Final)? SplitFinal<T>(BlockNode block) where T : IVisitableAstNode {
        if (block.Statements is not [.., InstructionBoundaryNode last]
            || last.Body.Statements is not [.., T final]) {
            return null;
        }
        InstructionBoundaryNode lastWithoutFinal = last with {
            Body = Sequence(last.Body.Statements.Take(last.Body.Statements.Count - 1))
        };
        return ([.. block.Statements.Take(block.Statements.Count - 1), lastWithoutFinal], final);
    }

    private static void CheckLoopHeaders(IReadOnlyList<LoweredBlock> blocks, NaturalLoopForest loops, string methodName) {
        HashSet<CfgBlock> checkedBlocks = blocks.Where(block => block.HasEntryCheck).Select(block => block.Block).ToHashSet();
        foreach (NaturalLoop loop in loops.Loops) {
            if (!checkedBlocks.Contains(loop.Header)) {
                throw new InvalidOperationException(
                    $"Generated method {methodName}: loop header {loop.Header.Entry.Address} has no CheckExternalEvents at its entry.");
            }
        }
    }

    /// <summary>The parts of a <c>do</c> loop: the latch block without its final <c>if</c>, the loop condition and the statement after the loop.</summary>
    private sealed record DoWhileShape(BlockNode LatchBody, ValueNode Condition, IVisitableAstNode Exit);

    /// <summary>Structures the regions of one method: the method body, loop bodies and <c>if</c> arms.</summary>
    private sealed class RegionWalker(NaturalLoopForest loops, IReadOnlySet<ICfgNode> entryNodes,
        IReadOnlyDictionary<ICfgNode, int> gotoCountByTarget, Dictionary<GotoNode, GotoKeepReason> refusals,
        string methodName, bool placeLoopExits) {
        /// <summary>
        /// The statements of <paramref name="members"/>, blocks in layout order: the header of a structurable loop other
        /// than <paramref name="enclosing"/> starts that loop, built from its blocks among <paramref name="members"/>; a
        /// block that ends with a forward branch may get one arm nested in an <c>if</c>.
        /// </summary>
        /// <param name="enclosing">The loop whose body <paramref name="members"/> are part of, or <c>null</c> outside every loop.</param>
        public BlockNode Walk(IReadOnlyList<LoweredBlock> members, NaturalLoop? enclosing) {
            List<LoweredBlock> order = [.. members];
            List<IVisitableAstNode> statements = [];
            HashSet<CfgBlock> placed = [];
            for (int index = 0; index < order.Count; index++) {
                LoweredBlock block = order[index];
                if (placed.Contains(block.Block)) {
                    continue;
                }
                if (loops.LoopWithHeader(block.Block) is NaturalLoop loop && loop != enclosing && IsStructurable(loop, entryNodes)) {
                    List<LoweredBlock> loopMembers = members.Where(member => loop.Contains(member.Block)).ToList();
                    if (!ReferenceEquals(loopMembers[0].Block, loop.Header)) {
                        throw new InvalidOperationException(
                            $"Generated method {methodName}: loop header {loop.Header.Entry.Address} is not the first block of its loop in the layout.");
                    }
                    statements.Add(LoopStatement(loop, loopMembers));
                    placed.UnionWith(loopMembers.Select(member => member.Block));
                    if (placeLoopExits) {
                        MoveExitAfterLoop(order, index, loop, loopMembers, placed);
                    }
                    continue;
                }
                placed.Add(block.Block);
                if (BranchStatement(block, members, placed, enclosing) is BlockNode withIf) {
                    statements.Add(withIf);
                    continue;
                }
                if (DispatchStatement(block, members, placed, enclosing) is BlockNode withSwitch) {
                    statements.Add(withSwitch);
                    continue;
                }
                statements.Add(block.Body);
            }
            return Sequence(statements);
        }

        private void Record(GotoNode? jump, GotoKeepReason? reason) {
            if (jump is not null && reason is not null) {
                refusals[jump] = reason.Value;
            }
        }

        private IVisitableAstNode LoopStatement(NaturalLoop loop, IReadOnlyList<LoweredBlock> members) {
            if (DoWhileShapeOf(loop, members) is DoWhileShape shape) {
                List<LoweredBlock> bodyMembers = [.. members.Take(members.Count - 1), members[^1] with { Body = shape.LatchBody }];
                BlockNode body = Walk(bodyMembers, loop);
                return Sequence(new DoWhileNode(body, shape.Condition), shape.Exit);
            }
            return new InfiniteLoopNode(Walk(members, loop));
        }

        /// <summary>
        /// Moves the exit of <paramref name="loop"/> that the loop's <c>goto</c>s target the most right after the loop
        /// in <paramref name="order"/> (in front of the first unplaced block after <paramref name="index"/>), so the
        /// <c>goto</c>s to it print as <c>break</c>s. The exit of a <c>do</c> loop is the target of the goto after its
        /// latch; for a <c>while (true)</c> loop it is the block outside the loop the most <c>goto</c>s of the loop
        /// members' bodies target, ties broken on the earliest in <paramref name="order"/>. The exit is moved only
        /// when it is an unplaced block of <paramref name="order"/> other than the next block and strictly more
        /// targeted than that next block (the <c>do</c> loop's exit goto counts as one). Every other unplaced block
        /// the exit dominates moves with it, keeping their relative order, so its fall-throughs stay fall-throughs.
        /// It may make the method worse (the moved exit can break fall-throughs of the blocks it jumps over), so
        /// <see cref="MethodEmitter"/> structures the method both with and without the move and keeps the result with
        /// fewer <c>goto</c>s.
        /// </summary>
        private void MoveExitAfterLoop(List<LoweredBlock> order, int index, NaturalLoop loop,
            IReadOnlyList<LoweredBlock> loopMembers, HashSet<CfgBlock> placed) {
            Dictionary<ICfgNode, int> exitCountByTarget = loopMembers.SelectMany(member => Gotos(member.Body))
                .CountBy(jump => jump.Target).ToDictionary(pair => pair.Key, pair => pair.Value);
            ICfgNode? preferredTarget = null;
            if (DoWhileShapeOf(loop, loopMembers) is DoWhileShape shape && shape.Exit is GotoNode exitGoto) {
                preferredTarget = exitGoto.Target;
            } else {
                int most = 0;
                foreach (LoweredBlock candidate in order) {
                    if (placed.Contains(candidate.Block) || loop.Contains(candidate.Block)) {
                        continue;
                    }
                    int count = exitCountByTarget.GetValueOrDefault(candidate.Block.Entry);
                    if (count > most) {
                        most = count;
                        preferredTarget = candidate.Block.Entry;
                    }
                }
            }
            if (preferredTarget is null) {
                return;
            }
            int exitIndex = order.FindIndex(held => ReferenceEquals(held.Block.Entry, preferredTarget));
            if (exitIndex < 0 || placed.Contains(order[exitIndex].Block)) {
                return;
            }
            int nextIndex = -1;
            for (int position = index + 1; position < order.Count; position++) {
                if (!placed.Contains(order[position].Block)) {
                    nextIndex = position;
                    break;
                }
            }
            if (nextIndex < 0 || exitIndex == nextIndex) {
                return;
            }
            int preferredCount = exitCountByTarget.GetValueOrDefault(preferredTarget);
            int nextCount = exitCountByTarget.GetValueOrDefault(order[nextIndex].Block.Entry);
            if (preferredCount <= nextCount) {
                return;
            }
            LoweredBlock exit = order[exitIndex];
            List<LoweredBlock> run = order.Skip(index + 1).Where(held => !placed.Contains(held.Block)
                && (ReferenceEquals(held, exit) || loops.Dominators.Dominates(exit.Block, held.Block))).ToList();
            foreach (LoweredBlock held in run) {
                order.Remove(held);
            }
            order.InsertRange(nextIndex, run);
        }

        private DoWhileShape? DoWhileShapeOf(NaturalLoop loop, IReadOnlyList<LoweredBlock> members) {
            LoweredBlock latch = members[^1];
            if (loop.Latches is not [CfgBlock onlyLatch] || !ReferenceEquals(onlyLatch, latch.Block)
                || loops.InnermostLoopOf(latch.Block) != loop) {
                return null;
            }
            if (SplitFinal<IfElseNode>(latch.Body) is not (List<IVisitableAstNode> head, IfElseNode branch)
                || !ReadsOnlyMachineState(branch.Condition)) {
                return null;
            }
            ICfgNode header = loop.Header.Entry;
            ValueNode condition;
            IVisitableAstNode exitArm;
            if (IsOnlyGotoTo(branch.TrueCase, header)) {
                condition = branch.Condition;
                exitArm = branch.FalseCase;
            } else if (IsOnlyGotoTo(branch.FalseCase, header)) {
                condition = NegatedCondition(branch.Condition);
                exitArm = branch.TrueCase;
            } else {
                return null;
            }
            if (ExitStatement(exitArm, header) is not IVisitableAstNode exit) {
                return null;
            }
            BlockNode latchBody = Sequence(head);
            return new DoWhileShape(latchBody, condition, exit);
        }

        /// <summary>
        /// <paramref name="block"/> with the arms of its final forward branch nested: in an <c>if</c> when exactly one arm
        /// comes back to the other target, in an <c>if</c> / <c>else</c> when neither does and both arms meet at one join
        /// block, or in an <c>if</c> without <c>else</c> when an arm diverges (the arm whose head is later in layout order
        /// when both do); <c>null</c> otherwise. Marks the nested blocks as placed.
        /// </summary>
        private BlockNode? BranchStatement(LoweredBlock block, IReadOnlyList<LoweredBlock> members, HashSet<CfgBlock> placed,
            NaturalLoop? enclosing) {
            if (SplitFinal<IfElseNode>(block.Body) is not (List<IVisitableAstNode> head, IfElseNode branch)) {
                return null;
            }
            GotoNode? trueGoto = OnlyGoto(branch.TrueCase);
            GotoNode? falseGoto = OnlyGoto(branch.FalseCase);
            if (!ReadsOnlyMachineState(branch.Condition)) {
                Record(trueGoto, GotoKeepReason.ConditionReadsLocals);
                Record(falseGoto, GotoKeepReason.ConditionReadsLocals);
                return null;
            }
            if (trueGoto is null || falseGoto is null) {
                Record(trueGoto, GotoKeepReason.OtherArmNotGoto);
                Record(falseGoto, GotoKeepReason.OtherArmNotGoto);
                return null;
            }
            ICfgNode whenTrue = trueGoto.Target;
            ICfgNode whenFalse = falseGoto.Target;
            if (ReferenceEquals(whenTrue, whenFalse)) {
                Record(trueGoto, GotoKeepReason.NotNextInLayout);
                Record(falseGoto, GotoKeepReason.NotNextInLayout);
                return null;
            }
            (List<LoweredBlock>? trueArm, GotoKeepReason? trueFailure) = ArmBlocksWithReason(whenTrue, members, placed, enclosing);
            (List<LoweredBlock>? falseArm, GotoKeepReason? falseFailure) = ArmBlocksWithReason(whenFalse, members, placed, enclosing);
            bool trueComesBack = trueArm is not null && ComesBackTo(trueArm, whenFalse);
            bool falseComesBack = falseArm is not null && ComesBackTo(falseArm, whenTrue);
            if (OneArmComesBack(head, branch, trueArm, falseArm, trueComesBack, falseComesBack, whenTrue, whenFalse,
                    placed, enclosing) is BlockNode oneArm) {
                return oneArm;
            }
            if (IfElseWithJoin(head, branch, trueArm, falseArm, trueComesBack, falseComesBack, placed, enclosing) is BlockNode ifElse) {
                return ifElse;
            }
            if (GuardClause(head, branch, trueArm, falseArm, members, whenTrue, whenFalse, placed, enclosing) is BlockNode guard) {
                return guard;
            }
            RecordArmFailure(trueGoto, trueArm, trueFailure, falseArm);
            RecordArmFailure(falseGoto, falseArm, falseFailure, trueArm);
            return null;
        }

        /// <summary>
        /// One arm comes back to the other target: it nests in an <c>if</c> and the other target follows the <c>if</c>.
        /// </summary>
        private BlockNode? OneArmComesBack(List<IVisitableAstNode> head, IfElseNode branch, List<LoweredBlock>? trueArm,
            List<LoweredBlock>? falseArm, bool trueComesBack, bool falseComesBack, ICfgNode whenTrue, ICfgNode whenFalse,
            HashSet<CfgBlock> placed, NaturalLoop? enclosing) {
            if (trueArm is not null && trueComesBack && !falseComesBack) {
                return Nested(head, branch.Condition, trueArm, null, whenFalse, placed, enclosing);
            }
            if (falseArm is not null && falseComesBack && !trueComesBack) {
                return Nested(head, NegatedCondition(branch.Condition), falseArm, null, whenTrue, placed, enclosing);
            }
            return null;
        }

        /// <summary>Two arms that neither come back nor diverge and meet at one join nest in an <c>if</c> / <c>else</c>.</summary>
        private BlockNode? IfElseWithJoin(List<IVisitableAstNode> head, IfElseNode branch, List<LoweredBlock>? trueArm,
            List<LoweredBlock>? falseArm, bool trueComesBack, bool falseComesBack, HashSet<CfgBlock> placed,
            NaturalLoop? enclosing) {
            if (trueComesBack || falseComesBack || trueArm is null || falseArm is null
                || CommonJoin(trueArm, falseArm) is not ICfgNode join) {
                return null;
            }
            return Nested(head, branch.Condition, trueArm, falseArm, join, placed, enclosing);
        }

        /// <summary>
        /// An arm that never leaves (a guard clause) nests in an <c>if</c> without <c>else</c>; when both arms diverge,
        /// the arm whose head is later in layout order.
        /// </summary>
        private BlockNode? GuardClause(List<IVisitableAstNode> head, IfElseNode branch, List<LoweredBlock>? trueArm,
            List<LoweredBlock>? falseArm, IReadOnlyList<LoweredBlock> members, ICfgNode whenTrue, ICfgNode whenFalse,
            HashSet<CfgBlock> placed, NaturalLoop? enclosing) {
            if (trueArm is not null && Diverges(trueArm) && NestDivergingArm(members, trueArm, falseArm)) {
                return Nested(head, branch.Condition, trueArm, null, whenFalse, placed, enclosing);
            }
            if (falseArm is not null && Diverges(falseArm)) {
                return Nested(head, NegatedCondition(branch.Condition), falseArm, null, whenTrue, placed, enclosing);
            }
            return null;
        }

        private bool Diverges(List<LoweredBlock> arm) => Exits(arm).Count == 0;

        private bool NestDivergingArm(IReadOnlyList<LoweredBlock> members, List<LoweredBlock> arm, List<LoweredBlock>? otherArm) {
            if (otherArm is null || !Diverges(otherArm)) {
                return true;
            }
            return LayoutIndex(members, arm[0]) >= LayoutIndex(members, otherArm[0]);
        }

        /// <summary>The index of the first member that is <paramref name="block"/>, or <paramref name="members"/>' count.</summary>
        private static int LayoutIndex(IReadOnlyList<LoweredBlock> members, LoweredBlock block) {
            for (int index = 0; index < members.Count; index++) {
                if (ReferenceEquals(members[index], block)) {
                    return index;
                }
            }
            return members.Count;
        }

        /// <summary>Records why <paramref name="jump"/>'s arm could not nest: <paramref name="failure"/> when it has no
        /// arm, <see cref="GotoKeepReason.ArmNeverComesBack"/> when the other arm does not exist, otherwise
        /// <see cref="GotoKeepReason.NoSingleJoin"/>.</summary>
        private void RecordArmFailure(GotoNode jump, List<LoweredBlock>? arm, GotoKeepReason? failure, List<LoweredBlock>? otherArm) {
            if (arm is null) {
                Record(jump, failure);
                return;
            }
            if (otherArm is null) {
                Record(jump, GotoKeepReason.ArmNeverComesBack);
                return;
            }
            Record(jump, GotoKeepReason.NoSingleJoin);
        }

        /// <summary>
        /// <paramref name="head"/>, then <c>if (condition) { then arm } else { else arm }</c> (no <c>else</c> when
        /// <paramref name="elseArm"/> is <c>null</c>), then <c>goto after</c>. Marks the arms' blocks as placed.
        /// </summary>
        private BlockNode Nested(IReadOnlyList<IVisitableAstNode> head, ValueNode condition, List<LoweredBlock> thenArm,
            List<LoweredBlock>? elseArm, ICfgNode after, HashSet<CfgBlock> placed, NaturalLoop? enclosing) {
            placed.UnionWith(thenArm.Select(member => member.Block));
            BlockNode elseBody = Empty();
            if (elseArm is not null) {
                placed.UnionWith(elseArm.Select(member => member.Block));
                elseBody = Walk(elseArm, enclosing);
            }
            IfElseNode nested = new(condition, Walk(thenArm, enclosing), elseBody);
            return Sequence(head.Append(nested).Append(new GotoNode(after)));
        }

        /// <summary>
        /// The blocks of the arm that starts at <paramref name="head"/>, in layout order, or <c>null</c> when that arm
        /// cannot be nested: its head is placed, targeted by more than one <c>goto</c>, a loop header or a partition
        /// entry, or the arm holds a latch of <paramref name="enclosing"/>.
        /// </summary>
        private List<LoweredBlock>? ArmBlocks(ICfgNode head, IReadOnlyList<LoweredBlock> members, HashSet<CfgBlock> placed,
            NaturalLoop? enclosing) =>
            ArmBlocksWithReason(head, members, placed, enclosing).Arm;

        /// <summary>The arm of <see cref="ArmBlocks"/>, or the reason it cannot be nested.</summary>
        private (List<LoweredBlock>? Arm, GotoKeepReason? Reason) ArmBlocksWithReason(ICfgNode head,
            IReadOnlyList<LoweredBlock> members, HashSet<CfgBlock> placed, NaturalLoop? enclosing) {
            LoweredBlock? headBlock = members.FirstOrDefault(member => ReferenceEquals(member.Block.Entry, head));
            if (headBlock is null) {
                return (null, GotoKeepReason.ArmOutsideRegion);
            }
            if (placed.Contains(headBlock.Block)) {
                return (null, GotoKeepReason.ArmHeadPlaced);
            }
            if (gotoCountByTarget.GetValueOrDefault(head) != 1) {
                return (null, GotoKeepReason.SharedTarget);
            }
            if (loops.LoopWithHeader(headBlock.Block) is not null) {
                return (null, GotoKeepReason.ArmHeadIsLoopHeader);
            }
            if (headBlock.Block.Instructions.Any(entryNodes.Contains)) {
                return (null, GotoKeepReason.TargetIsEntry);
            }
            List<LoweredBlock> arm = members
                .Where(member => !placed.Contains(member.Block) && loops.Dominators.Dominates(headBlock.Block, member.Block))
                .ToList();
            if (enclosing is not null && arm.Any(member => enclosing.Latches.Contains(member.Block))) {
                return (null, GotoKeepReason.ArmHoldsLatch);
            }
            if (!ReferenceEquals(arm[0], headBlock)) {
                throw new InvalidOperationException(
                    $"Generated method {methodName}: arm head {head.Address} is not the first block of its arm in the layout.");
            }
            return (arm, null);
        }

        private bool ComesBackTo(List<LoweredBlock> arm, ICfgNode target) =>
            arm.Any(member => loops.Graph.GetSuccessors(member.Block).Any(successor => ReferenceEquals(successor.Entry, target)));

        /// <summary>The entry of the only block that both arms exit to, or <c>null</c> when there is none or more than one.</summary>
        private ICfgNode? CommonJoin(List<LoweredBlock> first, List<LoweredBlock> second) {
            HashSet<CfgBlock> firstExits = Exits(first);
            List<CfgBlock> common = Exits(second).Where(firstExits.Contains).ToList();
            if (common is [CfgBlock join]) {
                return join.Entry;
            }
            return null;
        }

        private HashSet<CfgBlock> Exits(List<LoweredBlock> arm) {
            HashSet<CfgBlock> inside = arm.Select(member => member.Block).ToHashSet();
            return arm.SelectMany(member => loops.Graph.GetSuccessors(member.Block))
                .Where(successor => !inside.Contains(successor))
                .ToHashSet();
        }

        /// <summary>
        /// <paramref name="block"/> with the arms of its final near jump dispatch nested in their <c>case</c> sections, or
        /// <c>null</c> when fewer than two sections take their arm. A section takes the arm of its <c>goto</c> target when
        /// that arm can be nested and never leaves, or leaves only to the join: the only exit shared by every candidate
        /// arm that has exactly one exit. Marks the nested blocks as placed.
        /// </summary>
        private BlockNode? DispatchStatement(LoweredBlock block, IReadOnlyList<LoweredBlock> members, HashSet<CfgBlock> placed,
            NaturalLoop? enclosing) {
            if (SplitFinal<SwitchNode>(block.Body) is not (List<IVisitableAstNode> head, SwitchNode dispatch)) {
                return null;
            }
            if (!ReadsOnlyMachineState(dispatch.Value)) {
                RecordFlatSections(dispatch.Sections, []);
                return null;
            }
            Dictionary<int, List<LoweredBlock>> armByLabel = dispatch.Sections
                .SelectMany(section => SectionArm(section, members, placed, enclosing)).ToDictionary();
            List<CfgBlock> onlyExits = armByLabel.Values.Select(Exits).Where(exits => exits.Count == 1)
                .Select(exits => exits.Single()).Distinct().ToList();
            CfgBlock? join = null;
            if (onlyExits is [CfgBlock single]) {
                join = single;
            }
            List<int> nestedLabels = armByLabel.Where(pair => IsCaseArm(pair.Value, join)).Select(pair => pair.Key).ToList();
            if (nestedLabels.Count < 2) {
                RecordFlatSections(dispatch.Sections, []);
                return null;
            }
            foreach (int label in nestedLabels) {
                placed.UnionWith(armByLabel[label].Select(member => member.Block));
            }
            List<SwitchSection> sections = [];
            foreach (SwitchSection section in dispatch.Sections) {
                if (nestedLabels.Contains(section.Label)) {
                    sections.Add(section with { Body = Walk(armByLabel[section.Label], enclosing) });
                } else {
                    sections.Add(section);
                }
            }
            RecordFlatSections(sections, nestedLabels);
            List<IVisitableAstNode> statements = [.. head, dispatch with { Sections = sections }];
            if (join is not null) {
                statements.Add(new GotoNode(join.Entry));
            }
            return Sequence(statements);
        }

        /// <summary>Records <see cref="GotoKeepReason.SwitchSection"/> for each section of <paramref name="sections"/>
        /// whose label is not in <paramref name="nestedLabels"/> and whose body is a single <c>goto</c>.</summary>
        private void RecordFlatSections(IReadOnlyList<SwitchSection> sections, IReadOnlyCollection<int> nestedLabels) {
            foreach (SwitchSection section in sections.Where(section => !nestedLabels.Contains(section.Label))) {
                Record(OnlyGoto(section.Body), GotoKeepReason.SwitchSection);
            }
        }

        /// <summary>The label and arm of <paramref name="section"/> when its body is a single <c>goto</c> to an arm
        /// that can be nested (see <see cref="ArmBlocks"/>), otherwise nothing.</summary>
        private IEnumerable<KeyValuePair<int, List<LoweredBlock>>> SectionArm(SwitchSection section,
            IReadOnlyList<LoweredBlock> members, HashSet<CfgBlock> placed, NaturalLoop? enclosing) {
            if (OnlyGotoTarget(section.Body) is ICfgNode target
                && ArmBlocks(target, members, placed, enclosing) is List<LoweredBlock> arm) {
                yield return new(section.Label, arm);
            }
        }

        private bool IsCaseArm(List<LoweredBlock> arm, CfgBlock? join) {
            HashSet<CfgBlock> exits = Exits(arm);
            return exits.Count == 0 || (join is not null && exits.Count == 1 && exits.Contains(join));
        }
    }
}
