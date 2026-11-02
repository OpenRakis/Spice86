namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.Graph;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

/// <summary>
/// Rewrites a <see cref="CfgInstruction"/>'s execution AST by lifting it to IR and structuring it back:
/// raw flattening with <see cref="IrFlattener"/>, lowering with <see cref="IrBlockLowering"/>, then
/// <see cref="MethodStructurizer"/>, <see cref="FallthroughGotoRemover"/> and
/// <see cref="WhileConditionLoops"/>. The rebuilt AST is converted back to CPU nodes; a shape the
/// structurizer leaves behind (a <c>goto</c>, a label, <c>while (true)</c>, a <c>continue</c>) makes the
/// rewrite throw, because the JIT cannot execute it. Any other node is returned unchanged.
/// </summary>
internal sealed class GeneratorIrRoundTrip : IExecutionAstRewriter {
    /// <inheritdoc />
    public IVisitableAstNode Rewrite(ICfgNode node, IVisitableAstNode executionAst) {
        if (node is not CfgInstruction instruction) {
            return executionAst;
        }
        try {
            return RewriteInstruction(instruction, executionAst);
        } catch (InvalidOperationException exception) {
            throw RoundTripFailure(instruction, exception);
        } catch (ArgumentException exception) {
            throw RoundTripFailure(instruction, exception);
        }
    }

    private static NotSupportedException RoundTripFailure(CfgInstruction instruction, Exception exception) =>
        new($"IR round trip failed for {instruction.Address}: {exception.Message}", exception);

    private static IVisitableAstNode RewriteInstruction(CfgInstruction instruction,
        IVisitableAstNode executionAst) {
        IrFlattener flattener = new(true);
        IrBlock start = flattener.StartBlock(instruction, true, null);
        flattener.Flatten(executionAst, instruction, string.Empty, _ => false);
        if (flattener.PendingJumps.Count > 0) {
            throw new NotSupportedException(
                $"Execution AST of {instruction.Address} holds a goto to another CFG node.");
        }
        foreach (IrBlock block in flattener.Blocks.Where(block => !block.HasTerminator)) {
            block.Terminator = new IrExit(instruction,
                new MoveIpNextNode(new ConstantNode(DataType.UINT32,
                    instruction.NextInMemoryAddress32.Offset)));
        }
        IrMethod method = new($"roundtrip_{instruction.Address}", flattener.Blocks,
            [new IrEntry(instruction, start)], new HashSet<ICfgNode> { instruction },
            new Dictionary<ICfgNode, IrInstructionShape>());
        IrJumpThreading.Apply(method);
        List<LoweredBlock<IrBlock>> lowered = [.. IrBlockLowering.Lower(method, method.Blocks)];
        HashSet<IGotoTarget> entryTargets = [start];
        BlockNode structured = MethodStructurizer.Structure(lowered, Loops(method, start), method.Name,
            entryTargets, new Dictionary<GotoNode, GotoKeepReason>(ReferenceEqualityComparer.Instance),
            placeLoopExits: false, allowLocalsDeclaredInBlock: true, localsDeclaredInDominatingBlocks: true);
        BlockNode body = WhileConditionLoops.Apply(FallthroughGotoRemover.Remove(structured));
        try {
            return Sequence(Restore(body.Statements, instruction));
        } catch (NotSupportedException exception) {
            throw new NotSupportedException($"{exception.Message}\n{Dump(body, 0)}", exception);
        }
    }

    private static string Dump(IVisitableAstNode node, int indent) {
        string pad = new(' ', indent * 2);
        string label = node switch {
            GotoNode jump => $"GotoNode -> {jump.Target}",
            LabelNode labelNode => $"LabelNode {labelNode.Target}",
            InstructionBoundaryNode boundary => $"Boundary[{boundary.Node}] anchor={boundary.Anchor}",
            IfElseNode ifElse => $"IfElseNode({ifElse.Condition})",
            _ => node.GetType().Name
        };
        System.Text.StringBuilder builder = new($"{pad}{label}\n");
        IEnumerable<IVisitableAstNode> children = node switch {
            InstructionBoundaryNode boundary => boundary.Body.Statements,
            BlockNode block => block.Statements,
            IfElseNode ifElse => [ifElse.TrueCase, ifElse.FalseCase],
            WhileNode loop => [loop.Body],
            PreTestLoopNode loop => [loop.Body],
            InfiniteLoopNode loop => [loop.Body],
            SwitchNode dispatch => dispatch.Sections.Select(section => (IVisitableAstNode)section.Body)
                .Append(dispatch.Default),
            _ => []
        };
        foreach (IVisitableAstNode child in children) {
            builder.Append(Dump(child, indent + 1));
        }
        return builder.ToString();
    }

    /// <summary>The natural loops of the instruction's own blocks, over inside edges only.</summary>
    private static NaturalLoopForest<IrBlock> Loops(IrMethod method, IrBlock start) {
        List<IrBlock> members = [.. method.Blocks];
        Dictionary<IrBlock, IReadOnlyList<IrBlock>> successors = members
            .ToDictionary<IrBlock, IrBlock, IReadOnlyList<IrBlock>>(block => block,
                block => [.. block.Terminator.Edges
                    .Where(edge => edge.InsideInstruction && members.Contains(edge.Target))
                    .Select(edge => edge.Target)
                    .Distinct()]);
        BlockGraph<IrBlock> graph = new(members, [start], successors,
            members.ToDictionary(block => block, block => (IrBlock?)null),
            Comparer<IrBlock>.Create((first, second) => first.Id.CompareTo(second.Id)),
            block => $"IR block {block.Id}");
        DepthFirstOrdering<IrBlock> traversal = new([.. graph.EntryBlocks, .. graph.Blocks],
            graph.GetSuccessors);
        return NaturalLoopForest<IrBlock>.Build(graph, traversal.RetreatingEdges);
    }

    /// <summary>The restored statements of a sequence: typed nodes unwrapped, boundaries inlined.</summary>
    private static List<IVisitableAstNode> Restore(IReadOnlyList<IVisitableAstNode> statements,
        CfgInstruction instruction) {
        List<IVisitableAstNode> restored = [];
        foreach (IVisitableAstNode statement in statements) {
            if (statement is InstructionBoundaryNode boundary) {
                restored.AddRange(Restore(boundary.Body.Statements, instruction));
            } else {
                restored.Add(RestoreNode(statement, instruction));
            }
        }
        return FoldExitElseArms(restored);
    }

    /// <summary>
    /// <c>if (c) { A } else { }</c> followed by statements <c>R</c>, where <c>A</c> ends with a transfer
    /// that never returns, becomes <c>if (c) { A } else { R }</c>; the mirrored false-arm case moves
    /// <c>R</c> into the true arm. A JIT exit node does not return, so the tail only runs on the
    /// complementary arm.
    /// </summary>
    private static List<IVisitableAstNode> FoldExitElseArms(List<IVisitableAstNode> statements) {
        for (int i = 0; i + 1 < statements.Count; i++) {
            if (statements[i] is not IfElseNode ifElse) {
                continue;
            }
            List<IVisitableAstNode> tail = statements.Skip(i + 1).ToList();
            if (IsEmpty(ifElse.FalseCase) && EndsWithExit(ifElse.TrueCase)) {
                statements[i] = new IfElseNode(ifElse.Condition, ifElse.TrueCase, Sequence(tail));
                statements.RemoveRange(i + 1, tail.Count);
            } else if (IsEmpty(ifElse.TrueCase) && EndsWithExit(ifElse.FalseCase)) {
                statements[i] = new IfElseNode(ifElse.Condition, Sequence(tail), ifElse.FalseCase);
                statements.RemoveRange(i + 1, tail.Count);
            }
        }
        return statements;
    }

    private static bool IsEmpty(IVisitableAstNode arm) => arm is BlockNode { Statements.Count: 0 };

    private static IReadOnlyList<IVisitableAstNode> ArmStatements(IVisitableAstNode arm) =>
        arm is BlockNode block ? block.Statements : [arm];

    private static bool EndsWithExit(IVisitableAstNode arm) {
        IReadOnlyList<IVisitableAstNode> statements = ArmStatements(arm);
        return statements.Count > 0 && IsExit(statements[^1]);
    }

    private static bool IsExit(IVisitableAstNode node) => node is JumpNearNode or JumpFarNode
        or CallNearNode or CallFarNode or MoveIpNextNode or HltNode or InvalidInstructionNode
        or CpuidNode or InterruptCallNode or ReturnNearNode or ReturnFarNode or ReturnInterruptNode
        or CallbackNode or ThrowNode or ReturnNode or InterruptReturnNode or FailAsUntestedNode
        or CyclicJumpNode;

    /// <summary>One statement restored: ALU nodes unwrapped, containers restored recursively.</summary>
    private static IVisitableAstNode RestoreNode(IVisitableAstNode node, CfgInstruction instruction) =>
        node switch {
            AluStatementNode wrapper => RestoreNode(wrapper.Call, instruction),
            OpaqueNode opaque => RestoreNode(opaque.Original, instruction),
            AluOperationNode => RestoreValue(node, instruction),
            BlockNode block => Sequence(Restore(block.Statements, instruction)),
            IfElseNode ifElse => new IfElseNode(RestoreValue(ifElse.Condition, instruction),
                Sequence(Restore(ArmStatements(ifElse.TrueCase), instruction)),
                Sequence(Restore(ArmStatements(ifElse.FalseCase), instruction))),
            WhileNode loop => RestoreWhile(loop, instruction),
            PreTestLoopNode preTestLoop => RestoreWhile(
                new WhileNode(preTestLoop.Condition, preTestLoop.Body), instruction),
            InfiniteLoopNode loop => RestoreInfiniteLoop(loop.Body, instruction),
            SwitchNode dispatch => RestoreSwitch(dispatch, instruction),
            MethodCallNode or MethodCallValueNode or ReturnNode or FailAsUntestedNode
                or VariableDeclarationNode or BinaryOperationNode or ConditionalValueNode
                or UnaryOperationNode or TypeConversionNode or ExplicitCastNode
                or SegmentedPointerNode or AbsolutePointerNode or SegmentedAddressNode
                => IrAstChildren.MapChildren(node,
                    child => RestoreArgument(child, instruction)),
            InterruptReturnNode interruptReturn => new ReturnInterruptNode(instruction, interruptReturn.Width),
            GotoNode jump => RestoreGoto(jump, instruction),
            LabelNode or ContinueNode or InstructionNode or SelectorNode or DoWhileNode
                or EntryDispatcherLabelNode => throw new NotSupportedException(
                    $"IR round trip of {instruction.Address} cannot rebuild a {node.GetType().Name}."),
            _ => node
        };

    private static IVisitableAstNode RestoreArgument(IVisitableAstNode node, CfgInstruction instruction) =>
        node is ValueNode ? RestoreValue(node, instruction) : RestoreNode(node, instruction);

    private static ValueNode RestoreValue(IVisitableAstNode node, CfgInstruction instruction) =>
        node switch {
            AluOperationNode wrapper => (ValueNode)RestoreNode(wrapper.Call, instruction),
            _ => (ValueNode)RestoreNode(node, instruction)
        };

    /// <summary>
    /// A <c>goto</c> to a statement-less exit block of the instruction is the block's transfer node
    /// again. Any other <c>goto</c> is control flow the CPU AST cannot express.
    /// </summary>
    private static IVisitableAstNode RestoreGoto(GotoNode jump, CfgInstruction instruction) {
        if (jump.Target is IrBlock target && target.Statements.Count == 0
            && target.Terminator is IrExit exit) {
            return RestoreNode(exit.Transfer, instruction);
        }
        string targetDescription = jump.Target is IrBlock irTarget ? DescribeTarget(irTarget) : jump.Target.GetType().Name;
        throw new NotSupportedException(
            $"IR round trip of {instruction.Address} cannot rebuild a {nameof(GotoNode)} to {targetDescription}.");
    }

    /// <summary>Describes an IR block goto target for an error message.</summary>
    private static string DescribeTarget(IrBlock block) =>
        $"IR block {block.Id} ({block.Statements.Count} statements, terminator {block.Terminator?.GetType().Name ?? "none"})";

    /// <summary>
    /// A <c>while (c)</c> whose body ends with <c>if (e) { break; }</c> is a
    /// <see cref="WhileWithExitNode"/> again; anything else stays a <see cref="WhileNode"/>.
    /// </summary>
    private static IVisitableAstNode RestoreWhile(WhileNode loop, CfgInstruction instruction) {
        ValueNode condition = RestoreValue(loop.Condition, instruction);
        if (condition is ConstantNode constant && constant.DataType == DataType.BOOL) {
            return RestoreInfiniteLoop(loop.Body, instruction);
        }
        List<IVisitableAstNode> body = Restore(loop.Body.Statements, instruction);
        if (body.Count > 0 && body[^1] is IfElseNode tail
            && ArmStatements(tail.TrueCase) is [BreakNode]
            && IsEmpty(tail.FalseCase)) {
            return new WhileWithExitNode(condition, Sequence(body[..^1]),
                RestoreValue(tail.Condition, instruction));
        }
        return new WhileNode(condition, Sequence(body));
    }

    /// <summary>
    /// A <c>while (true)</c> restores by its exit test: <c>if (e) { break; }</c> first is a
    /// <see cref="WhileNode"/> on the complementary condition, last is a <see cref="WhileWithExitNode"/>
    /// with a constant condition. A loop with no such test cannot be expressed as a CPU AST node.
    /// </summary>
    private static IVisitableAstNode RestoreInfiniteLoop(BlockNode loopBody, CfgInstruction instruction) {
        List<IVisitableAstNode> body = Restore(loopBody.Statements, instruction);
        if (body.Count > 0 && ExitTest(body[0]) is IfElseNode first) {
            return new WhileNode(new UnaryOperationNode(DataType.BOOL, UnaryOperation.NOT,
                RestoreValue(first.Condition, instruction)), Sequence(body[1..]));
        }
        if (body.Count > 0 && ExitTest(body[^1]) is IfElseNode last) {
            return new WhileWithExitNode(new ConstantNode(DataType.BOOL, 1), Sequence(body[..^1]),
                RestoreValue(last.Condition, instruction));
        }
        throw new NotSupportedException(
            $"IR round trip of {instruction.Address} cannot rebuild a loop without an exit condition.");
    }

    private static IfElseNode? ExitTest(IVisitableAstNode statement) =>
        statement is IfElseNode ifElse && ArmStatements(ifElse.TrueCase) is [BreakNode]
            && IsEmpty(ifElse.FalseCase) ? ifElse : null;

    private static IVisitableAstNode RestoreSwitch(SwitchNode dispatch, CfgInstruction instruction) {
        List<SwitchSection> sections = [];
        foreach (SwitchSection section in dispatch.Sections) {
            List<IVisitableAstNode> body = Restore(section.Body.Statements, instruction);
            if (body.Count > 0 && body[^1] is BreakNode) {
                body.RemoveAt(body.Count - 1);
            }
            sections.Add(section with { Body = Sequence(body) });
        }
        List<IVisitableAstNode> defaultBody = Restore(dispatch.Default.Statements, instruction);
        if (defaultBody.Count > 0 && defaultBody[^1] is BreakNode) {
            defaultBody.RemoveAt(defaultBody.Count - 1);
        }
        return new SwitchNode(RestoreValue(dispatch.Value, instruction), sections, Sequence(defaultBody));
    }
}
