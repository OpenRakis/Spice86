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
    /// / <c>else</c> (two arms that meet at one join).
    /// </summary>
    /// <param name="blocks">The lowered blocks, in layout order.</param>
    /// <param name="loops">The natural loops of the method's block graph.</param>
    /// <param name="entryNodes">The partition entry nodes of the method.</param>
    /// <param name="methodName">The method name, for error messages.</param>
    /// <exception cref="InvalidOperationException">A natural loop header does not start with <c>CheckExternalEvents</c>.</exception>
    /// <exception cref="InvalidOperationException">A loop header or an arm head is not the first block of its loop or arm in the layout.</exception>
    public static BlockNode Structure(IReadOnlyList<LoweredBlock> blocks, NaturalLoopForest loops, IReadOnlySet<ICfgNode> entryNodes, string methodName) {
        CheckLoopHeaders(blocks, loops, methodName);
        Dictionary<ICfgNode, int> gotoCountByTarget = new();
        foreach (LoweredBlock block in blocks) {
            CountGotos(block.Body, gotoCountByTarget);
        }
        return new RegionWalker(loops, entryNodes, gotoCountByTarget, methodName).Walk(blocks, null);
    }

    /// <summary>Adds one per <c>goto</c> of <paramref name="node"/> to the count of its target.</summary>
    private static void CountGotos(IVisitableAstNode node, Dictionary<ICfgNode, int> gotoCountByTarget) {
        switch (node) {
            case GotoNode jump:
                gotoCountByTarget[jump.Target] = gotoCountByTarget.GetValueOrDefault(jump.Target) + 1;
                return;
            case BlockNode block:
                foreach (IVisitableAstNode statement in block.Statements) {
                    CountGotos(statement, gotoCountByTarget);
                }
                return;
            case InstructionBoundaryNode boundary:
                CountGotos(boundary.Body, gotoCountByTarget);
                return;
            case IfElseNode ifElse:
                CountGotos(ifElse.TrueCase, gotoCountByTarget);
                CountGotos(ifElse.FalseCase, gotoCountByTarget);
                return;
            case WhileNode whileNode:
                CountGotos(whileNode.Body, gotoCountByTarget);
                return;
            case InfiniteLoopNode infiniteLoop:
                CountGotos(infiniteLoop.Body, gotoCountByTarget);
                return;
            case DoWhileNode doWhile:
                CountGotos(doWhile.Body, gotoCountByTarget);
                return;
            case SwitchNode switchNode:
                foreach (SwitchSection section in switchNode.Sections) {
                    CountGotos(section.Body, gotoCountByTarget);
                }
                CountGotos(switchNode.Default, gotoCountByTarget);
                return;
            case CpuFaultTryCatchNode tryCatch:
                CountGotos(tryCatch.Body, gotoCountByTarget);
                return;
        }
    }

    private static ICfgNode? OnlyGotoTarget(IVisitableAstNode arm) {
        if (Sequence(arm).Statements is [GotoNode jump]) {
            return jump.Target;
        }
        return null;
    }

    private static bool IsOnlyGotoTo(IVisitableAstNode arm, ICfgNode target) =>
        Sequence(arm).Statements is [GotoNode jump] && ReferenceEquals(jump.Target, target);

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

    private static bool ReadsOnlyMachineState(ValueNode node) => node switch {
        CpuFlagNode or RegisterNode or ConstantNode or InstructionFieldNode => true,
        UnaryOperationNode unary => ReadsOnlyMachineState(unary.Value),
        BinaryOperationNode binary => ReadsOnlyMachineState(binary.Left) && ReadsOnlyMachineState(binary.Right),
        TypeConversionNode conversion => ReadsOnlyMachineState(conversion.Value),
        SegmentedPointerNode pointer => ReadsOnlyMachineState(pointer.Segment) && ReadsOnlyMachineState(pointer.Offset)
            && (pointer.DefaultSegment is null || ReadsOnlyMachineState(pointer.DefaultSegment)),
        _ => false
    };

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
        IReadOnlyDictionary<ICfgNode, int> gotoCountByTarget, string methodName) {
        /// <summary>
        /// The statements of <paramref name="members"/>, blocks in layout order: the header of a structurable loop other
        /// than <paramref name="enclosing"/> starts that loop, built from its blocks among <paramref name="members"/>; a
        /// block that ends with a forward branch may get one arm nested in an <c>if</c>.
        /// </summary>
        /// <param name="enclosing">The loop whose body <paramref name="members"/> are part of, or <c>null</c> outside every loop.</param>
        public BlockNode Walk(IReadOnlyList<LoweredBlock> members, NaturalLoop? enclosing) {
            List<IVisitableAstNode> statements = [];
            HashSet<CfgBlock> placed = [];
            foreach (LoweredBlock block in members) {
                if (placed.Contains(block.Block)) {
                    continue;
                }
                if (loops.LoopWithHeader(block.Block) is NaturalLoop loop && loop != enclosing && IsStructurable(loop)) {
                    List<LoweredBlock> loopMembers = members.Where(member => loop.Contains(member.Block)).ToList();
                    if (!ReferenceEquals(loopMembers[0].Block, loop.Header)) {
                        throw new InvalidOperationException(
                            $"Generated method {methodName}: loop header {loop.Header.Entry.Address} is not the first block of its loop in the layout.");
                    }
                    statements.Add(LoopStatement(loop, loopMembers));
                    placed.UnionWith(loopMembers.Select(member => member.Block));
                    continue;
                }
                placed.Add(block.Block);
                if (BranchStatement(block, members, placed, enclosing) is BlockNode withIf) {
                    statements.Add(withIf);
                    continue;
                }
                statements.Add(block.Body);
            }
            return Sequence(statements);
        }

        private bool IsStructurable(NaturalLoop loop) {
            ICfgNode header = loop.Header.Entry;
            return !loop.Blocks.SelectMany(block => block.Instructions)
                .Any(node => entryNodes.Contains(node) && !ReferenceEquals(node, header));
        }

        private IVisitableAstNode LoopStatement(NaturalLoop loop, IReadOnlyList<LoweredBlock> members) {
            if (DoWhileShapeOf(loop, members) is DoWhileShape shape) {
                List<LoweredBlock> bodyMembers = [.. members.Take(members.Count - 1), members[^1] with { Body = shape.LatchBody }];
                BlockNode body = Walk(bodyMembers, loop);
                return Sequence(new DoWhileNode(body, shape.Condition), shape.Exit);
            }
            return new InfiniteLoopNode(Walk(members, loop));
        }

        private DoWhileShape? DoWhileShapeOf(NaturalLoop loop, IReadOnlyList<LoweredBlock> members) {
            LoweredBlock latch = members[^1];
            if (loop.Latches is not [CfgBlock onlyLatch] || !ReferenceEquals(onlyLatch, latch.Block)
                || loops.InnermostLoopOf(latch.Block) != loop) {
                return null;
            }
            if (latch.Body.Statements is not [.., InstructionBoundaryNode last]
                || last.Body.Statements is not [.., IfElseNode branch]
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
            InstructionBoundaryNode lastWithoutBranch = last with {
                Body = Sequence(last.Body.Statements.Take(last.Body.Statements.Count - 1))
            };
            BlockNode latchBody = Sequence(latch.Body.Statements.Take(latch.Body.Statements.Count - 1).Append(lastWithoutBranch));
            return new DoWhileShape(latchBody, condition, exit);
        }

        /// <summary>
        /// <paramref name="block"/> with the arms of its final forward branch nested: in an <c>if</c> when exactly one arm
        /// comes back to the other target, in an <c>if</c> / <c>else</c> when neither does and both arms meet at one join
        /// block; <c>null</c> otherwise. Marks the nested blocks as placed.
        /// </summary>
        private BlockNode? BranchStatement(LoweredBlock block, IReadOnlyList<LoweredBlock> members, HashSet<CfgBlock> placed,
            NaturalLoop? enclosing) {
            if (block.Body.Statements is not [.., InstructionBoundaryNode last]
                || last.Body.Statements is not [.., IfElseNode branch]
                || !ReadsOnlyMachineState(branch.Condition)
                || OnlyGotoTarget(branch.TrueCase) is not ICfgNode whenTrue
                || OnlyGotoTarget(branch.FalseCase) is not ICfgNode whenFalse
                || ReferenceEquals(whenTrue, whenFalse)) {
                return null;
            }
            List<LoweredBlock>? trueArm = ArmBlocks(whenTrue, members, placed, enclosing);
            List<LoweredBlock>? falseArm = ArmBlocks(whenFalse, members, placed, enclosing);
            bool trueComesBack = trueArm is not null && ComesBackTo(trueArm, whenFalse);
            bool falseComesBack = falseArm is not null && ComesBackTo(falseArm, whenTrue);
            InstructionBoundaryNode lastWithoutBranch = last with {
                Body = Sequence(last.Body.Statements.Take(last.Body.Statements.Count - 1))
            };
            List<IVisitableAstNode> head = [.. block.Body.Statements.Take(block.Body.Statements.Count - 1), lastWithoutBranch];
            if (trueArm is not null && trueComesBack && !falseComesBack) {
                return Nested(head, branch.Condition, trueArm, null, whenFalse, placed, enclosing);
            }
            if (falseArm is not null && falseComesBack && !trueComesBack) {
                return Nested(head, NegatedCondition(branch.Condition), falseArm, null, whenTrue, placed, enclosing);
            }
            if (trueComesBack || falseComesBack || trueArm is null || falseArm is null
                || CommonJoin(trueArm, falseArm) is not ICfgNode join) {
                return null;
            }
            return Nested(head, branch.Condition, trueArm, falseArm, join, placed, enclosing);
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
            NaturalLoop? enclosing) {
            LoweredBlock? headBlock = members.FirstOrDefault(member => ReferenceEquals(member.Block.Entry, head));
            if (headBlock is null || placed.Contains(headBlock.Block)
                || gotoCountByTarget.GetValueOrDefault(head) != 1
                || loops.LoopWithHeader(headBlock.Block) is not null
                || headBlock.Block.Instructions.Any(entryNodes.Contains)) {
                return null;
            }
            List<LoweredBlock> arm = members
                .Where(member => !placed.Contains(member.Block) && loops.Dominators.Dominates(headBlock.Block, member.Block))
                .ToList();
            if (enclosing is not null && arm.Any(member => enclosing.Latches.Contains(member.Block))) {
                return null;
            }
            if (!ReferenceEquals(arm[0], headBlock)) {
                throw new InvalidOperationException(
                    $"Generated method {methodName}: arm head {head.Address} is not the first block of its arm in the layout.");
            }
            return arm;
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
    }
}
