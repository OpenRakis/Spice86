namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;
using Spice86.Core.Emulator.ReverseEngineer.Graph;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

public class TailDuplicatorTest {
    private static readonly CpuFlagNode Zero = new(Flags.Zero);

    private static CfgBlock Block(int id, ushort offset) =>
        new(id, CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset)));

    private static InstructionBoundaryNode Boundary(CfgBlock block, params IVisitableAstNode[] body) =>
        new(block.Entry, "", new BlockNode(body));

    private static FailAsUntestedNode Untested() => new([UntestedMessagePart.Literal("Untested far call target at 1000:0000")]);

    private static bool IsGoto(IVisitableAstNode node) => node is GotoNode;

    private static bool IsBoundaryOf(IVisitableAstNode node, ICfgNode target) =>
        node is InstructionBoundaryNode boundary && ReferenceEquals(boundary.Node, target);

    private static NaturalLoopForest Forest(IReadOnlyList<CfgBlock> blocks, Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> successors) {
        PartitionBlockGraph graph = new(blocks, [blocks[0]], successors, new Dictionary<CfgBlock, CfgBlock?>());
        DepthFirstOrdering<CfgBlock> traversal = new([.. graph.EntryBlocks, .. graph.Blocks], graph.GetSuccessors);
        return NaturalLoopForest.Build(graph, traversal.RetreatingEdges);
    }

    /// <summary>A one-boundary block that ends in <c>throw FailAsUntested</c>: the smallest duplicable tail.</summary>
    private static LoweredBlock ThrowTail(CfgBlock block, string name) =>
        new(block, false, new BlockNode(Boundary(block, new CommentNode(name), Untested())));

    private static BlockNode Apply(BlockNode body, LoweredBlock tail, IReadOnlySet<ICfgNode> entryNodes, NaturalLoopForest loops) =>
        TailDuplicator.Apply(body, [tail], entryNodes, loops);

    [Fact]
    public void GotoToUnreachableThrowTailIsReplacedByTheTailBody() {
        // Arrange
        CfgBlock source = Block(1, 0x00);
        CfgBlock tail = Block(2, 0x10);
        BlockNode body = Sequence(
            new IfElseNode(Zero, new BlockNode(new GotoNode(tail.Entry)), Empty()),
            Untested(),
            Boundary(tail, new CommentNode("tail"), Untested()));
        NaturalLoopForest loops = Forest([source, tail], new() { [source] = [tail] });

        // Act
        BlockNode result = Apply(body, ThrowTail(tail, "tail"), new HashSet<ICfgNode>(), loops);

        // Assert
        result.Statements.Should().HaveCount(2);
        IfElseNode branch = result.Statements[0].Should().BeOfType<IfElseNode>().Which;
        Sequence(branch.TrueCase).Statements.Should().ContainSingle()
            .Which.Should().BeOfType<InstructionBoundaryNode>().Which.Node.Should().BeSameAs(tail.Entry);
        result.Statements.Should().NotContain(node => IsGoto(node));
        result.Statements.Should().NotContain(node => IsBoundaryOf(node, tail.Entry));
    }

    [Fact]
    public void GotoToReachableThrowTailIsReplacedButTheTailStays() {
        // Arrange
        CfgBlock source = Block(1, 0x00);
        CfgBlock tail = Block(2, 0x10);
        BlockNode body = Sequence(
            new IfElseNode(Zero, new BlockNode(new GotoNode(tail.Entry)), Empty()),
            new CommentNode("falls through"),
            Boundary(tail, new CommentNode("tail"), Untested()));
        NaturalLoopForest loops = Forest([source, tail], new() { [source] = [tail] });

        // Act
        BlockNode result = Apply(body, ThrowTail(tail, "tail"), new HashSet<ICfgNode>(), loops);

        // Assert
        result.Statements.Should().HaveCount(3);
        result.Statements.Should().NotContain(node => IsGoto(node));
        result.Statements[2].Should().BeOfType<InstructionBoundaryNode>().Which.Node.Should().BeSameAs(tail.Entry);
    }

    [Fact]
    public void NonDuplicableTailsKeepTheirGoto() {
        // Arrange
        CfgBlock source = Block(1, 0x00);
        CfgBlock tail = Block(2, 0x10);
        NaturalLoopForest loops = Forest([source, tail], new() { [source] = [tail] });
        BlockNode MakeBody() => Sequence(
            new IfElseNode(Zero, new BlockNode(new GotoNode(tail.Entry)), Empty()),
            Untested(),
            Boundary(tail, new CommentNode("tail"), Untested()));
        LoweredBlock tooLong = new(tail, false, new BlockNode(
            Boundary(tail, new CommentNode("one")), Boundary(tail, new CommentNode("two")),
            Boundary(tail, new CommentNode("three")), Boundary(tail, new CommentNode("four"), Untested())));
        LoweredBlock withLocal = new(tail, false, new BlockNode(Boundary(tail,
            new VariableDeclarationNode(DataType.UINT16, "local", new ConstantNode(DataType.UINT16, 0)), Untested())));
        LoweredBlock completing = new(tail, false, new BlockNode(Boundary(tail, new CommentNode("tail"))));

        // Act
        BlockNode longResult = Apply(MakeBody(), tooLong, new HashSet<ICfgNode>(), loops);
        BlockNode localResult = Apply(MakeBody(), withLocal, new HashSet<ICfgNode>(), loops);
        BlockNode entryResult = Apply(MakeBody(), ThrowTail(tail, "tail"), new HashSet<ICfgNode> { tail.Entry }, loops);
        BlockNode completingResult = Apply(MakeBody(), completing, new HashSet<ICfgNode>(), loops);

        // Assert
        foreach (BlockNode result in new[] { longResult, localResult, entryResult, completingResult }) {
            result.Statements.Should().HaveCount(3);
            Sequence(result.Statements[0] is IfElseNode branch ? branch.TrueCase : Empty()).Statements
                .Should().ContainSingle().Which.Should().BeOfType<GotoNode>();
            result.Statements[2].Should().BeOfType<InstructionBoundaryNode>().Which.Node.Should().BeSameAs(tail.Entry);
        }
    }

    [Fact]
    public void EveryGotoToTheTailIsReplaced() {
        // Arrange
        CfgBlock source = Block(1, 0x00);
        CfgBlock tail = Block(2, 0x10);
        BlockNode body = Sequence(
            new IfElseNode(Zero, new BlockNode(new GotoNode(tail.Entry)), new BlockNode(new GotoNode(tail.Entry))),
            Untested(),
            Boundary(tail, new CommentNode("tail"), Untested()));
        NaturalLoopForest loops = Forest([source, tail], new() { [source] = [tail] });

        // Act
        BlockNode result = Apply(body, ThrowTail(tail, "tail"), new HashSet<ICfgNode>(), loops);

        // Assert
        result.Statements.Should().HaveCount(2);
        result.Statements.Should().NotContain(node => IsGoto(node));
        IfElseNode branch = result.Statements[0].Should().BeOfType<IfElseNode>().Which;
        Sequence(branch.TrueCase).Statements.Should().ContainSingle()
            .Which.Should().BeOfType<InstructionBoundaryNode>().Which.Node.Should().BeSameAs(tail.Entry);
        Sequence(branch.FalseCase).Statements.Should().ContainSingle()
            .Which.Should().BeOfType<InstructionBoundaryNode>().Which.Node.Should().BeSameAs(tail.Entry);
    }
}
