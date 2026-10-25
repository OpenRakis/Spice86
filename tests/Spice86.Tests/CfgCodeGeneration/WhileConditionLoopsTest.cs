namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu.Blocks;

using System.Collections.Immutable;

using Xunit;

public class WhileConditionLoopsTest {
    private static readonly CpuFlagNode Carry = new(0x01);
    private static readonly VariableReferenceNode Local = new(DataType.BOOL, "local");
    private static readonly SegmentedAddress At = new(0x1000, 0x0010);

    private static InstructionBoundaryNode Boundary(params IVisitableAstNode[] statements) =>
        new(Substitute.For<ICfgNode>(), "", new BlockNode(statements));

    private static CheckExternalEventsNode Check => new(At);

    private static IfElseNode ExitIf(ValueNode condition) =>
        new(condition, new BlockNode(new BreakNode()), new BlockNode());

    [Fact]
    public void BoundaryHeldExitTestBecomesAWhileCondition() {
        // Arrange
        InstructionBoundaryNode prefix = Boundary(Check, new CommentNode("cmp"));
        InstructionBoundaryNode test = Boundary(new CommentNode("jcc"), ExitIf(Carry));
        InstructionBoundaryNode rest = Boundary(new MethodCallNode(null, "Body"));
        BlockNode input = new(new InfiniteLoopNode(new BlockNode(prefix, test, rest)));

        // Act
        BlockNode result = WhileConditionLoops.Apply(input);

        // Assert
        WhileNode loop = result.Statements.Should().ContainSingle().Which.Should().BeOfType<WhileNode>().Which;
        loop.Condition.Should().BeOfType<UnaryOperationNode>().Which.Value.Should().BeSameAs(Carry);
        loop.Body.Statements.Should().HaveCount(3);
        loop.Body.Statements[0].Should().BeOfType<InstructionBoundaryNode>()
            .Which.Body.Statements.Should().HaveCount(2);
        loop.Body.Statements[1].Should().BeOfType<InstructionBoundaryNode>()
            .Which.Body.Statements.Should().Equal(new CommentNode("jcc"));
        loop.Body.Statements[2].Should().BeOfType<InstructionBoundaryNode>().Which.Node.Should().BeSameAs(rest.Node);
    }

    [Fact]
    public void StatementLevelExitTestBecomesAWhileCondition() {
        // Arrange
        InstructionBoundaryNode prefix = Boundary(Check);
        InstructionBoundaryNode rest = Boundary(new MethodCallNode(null, "Body"));
        BlockNode input = new(new InfiniteLoopNode(new BlockNode(prefix, ExitIf(Carry), rest)));

        // Act
        BlockNode result = WhileConditionLoops.Apply(input);

        // Assert
        WhileNode loop = result.Statements.Should().ContainSingle().Which.Should().BeOfType<WhileNode>().Which;
        loop.Condition.Should().BeOfType<UnaryOperationNode>();
        loop.Body.Statements.Should().HaveCount(2);
        loop.Body.Statements[1].Should().BeOfType<InstructionBoundaryNode>().Which.Node.Should().BeSameAs(rest.Node);
    }

    [Fact]
    public void ASpeculativeGuardBeforeTheTestKeepsTheLoopInfinite() {
        // Arrange
        InstructionBoundaryNode guarded = Boundary(new SpeculativeGuardNode(At, "Sig"), ExitIf(Carry));
        BlockNode input = new(new InfiniteLoopNode(new BlockNode(guarded, Boundary(new MethodCallNode(null, "Body")))));

        // Act
        BlockNode result = WhileConditionLoops.Apply(input);

        // Assert
        result.Statements.Should().ContainSingle().Which.Should().BeOfType<InfiniteLoopNode>();
    }

    [Fact]
    public void AStatementBeforeTheTestKeepsTheLoopInfinite() {
        // Arrange
        InstructionBoundaryNode test = Boundary(new MethodCallNode(null, "Alu"), ExitIf(Carry));
        BlockNode input = new(new InfiniteLoopNode(new BlockNode(test)));

        // Act
        BlockNode result = WhileConditionLoops.Apply(input);

        // Assert
        result.Statements.Should().ContainSingle().Which.Should().BeOfType<InfiniteLoopNode>();
    }

    [Fact]
    public void AnElseArmOnTheTestKeepsTheLoopInfinite() {
        // Arrange
        IfElseNode withElse = new(Carry, new BlockNode(new BreakNode()), new BlockNode(new CommentNode("else")));
        InstructionBoundaryNode test = Boundary(withElse);
        BlockNode input = new(new InfiniteLoopNode(new BlockNode(test)));

        // Act
        BlockNode result = WhileConditionLoops.Apply(input);

        // Assert
        result.Statements.Should().ContainSingle().Which.Should().BeOfType<InfiniteLoopNode>();
    }

    [Fact]
    public void ATrueArmThatIsNotExactlyBreakKeepsTheLoopInfinite() {
        // Arrange
        IfElseNode twoStatements = new(Carry, new BlockNode(new CommentNode("c"), new BreakNode()), new BlockNode());
        InstructionBoundaryNode test = Boundary(twoStatements);
        BlockNode input = new(new InfiniteLoopNode(new BlockNode(test)));

        // Act
        BlockNode result = WhileConditionLoops.Apply(input);

        // Assert
        result.Statements.Should().ContainSingle().Which.Should().BeOfType<InfiniteLoopNode>();
    }

    [Fact]
    public void AConditionReadingALocalKeepsTheLoopInfinite() {
        // Arrange
        InstructionBoundaryNode test = Boundary(ExitIf(Local));
        BlockNode input = new(new InfiniteLoopNode(new BlockNode(test)));

        // Act
        BlockNode result = WhileConditionLoops.Apply(input);

        // Assert
        result.Statements.Should().ContainSingle().Which.Should().BeOfType<InfiniteLoopNode>();
    }

    [Fact]
    public void AGotoToAWhileLoopHeaderPlacesTheLabelBeforeTheWhile() {
        // Arrange
        CfgInstruction header = CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, 0x0107));
        InstructionBoundaryNode check = new(header, "", new BlockNode(Check));
        WhileNode loop = new(Carry, new BlockNode(check));
        BlockNode input = new(loop);

        // Act
        BlockNode result = LabelPlacer.Place(input, ImmutableHashSet.Create<ICfgNode>(header));

        // Assert
        result.Statements.Should().HaveCount(2);
        result.Statements[0].Should().BeOfType<LabelNode>().Which.Target.Should().BeSameAs(header);
        result.Statements[1].Should().BeOfType<WhileNode>().Which.Body.Statements.Should().ContainSingle()
            .Which.Should().BeOfType<InstructionBoundaryNode>().Which.Node.Should().BeSameAs(header);
    }
}
