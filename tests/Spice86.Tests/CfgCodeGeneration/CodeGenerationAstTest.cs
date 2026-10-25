namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Xunit;

public class CodeGenerationAstTest {
    private static readonly CpuFlagNode C1 = new(0x40);
    private static readonly CpuFlagNode C2 = new(0x80);
    private static readonly MethodCallNode Foo = new(null, "Foo");
    private static readonly MethodCallNode Bar = new(null, "Bar");

    [Fact]
    public void SequenceFlattensBlocks() {
        // Act
        BlockNode sequence = CodeGenerationAst.Sequence(new BlockNode(Foo), Bar, new BlockNode());

        // Assert
        sequence.Statements.Should().Equal(Foo, Bar);
    }

    [Fact]
    public void OnlyABlockWithoutStatementsIsEmpty() {
        // Assert
        CodeGenerationAst.IsEmpty(new BlockNode()).Should().BeTrue();
        CodeGenerationAst.IsEmpty(new BlockNode(Foo)).Should().BeFalse();
        CodeGenerationAst.IsEmpty(Foo).Should().BeFalse();
    }

    [Fact]
    public void SectionBreaksOnlyWhenItsBodyCompletesNormally() {
        // Arrange
        GotoNode jump = new(Substitute.For<ICfgNode>());

        // Act
        SwitchSection completes = CodeGenerationAst.Section(1, new BlockNode(Foo));
        SwitchSection diverges = CodeGenerationAst.Section(2, new BlockNode(jump));
        SwitchSection empty = CodeGenerationAst.Section(3, new BlockNode());

        // Assert
        completes.Body.Statements.Should().HaveCount(2);
        completes.Body.Statements[^1].Should().BeOfType<BreakNode>();
        diverges.Body.Statements.Should().ContainSingle().Which.Should().BeSameAs(jump);
        empty.Body.Statements.Should().ContainSingle().Which.Should().BeOfType<BreakNode>();
    }

    [Fact]
    public void NegatedConditionDistributesOverLogicalAnd() {
        // Arrange
        BinaryOperationNode conjunction = new(DataType.BOOL, C1, BinaryOperation.LOGICAL_AND, C2);

        // Act
        ValueNode negated = CodeGenerationAst.NegatedCondition(conjunction);

        // Assert
        BinaryOperationNode result = negated.Should().BeOfType<BinaryOperationNode>().Which;
        result.BinaryOperation.Should().Be(BinaryOperation.LOGICAL_OR);
        result.Left.Should().BeOfType<UnaryOperationNode>().Which.Value.Should().BeSameAs(C1);
        result.Right.Should().BeOfType<UnaryOperationNode>().Which.Value.Should().BeSameAs(C2);
    }

    [Fact]
    public void NegatedConditionDistributesOverLogicalOr() {
        // Arrange
        BinaryOperationNode disjunction = new(DataType.BOOL, C1, BinaryOperation.LOGICAL_OR, C2);

        // Act
        ValueNode negated = CodeGenerationAst.NegatedCondition(disjunction);

        // Assert
        BinaryOperationNode result = negated.Should().BeOfType<BinaryOperationNode>().Which;
        result.BinaryOperation.Should().Be(BinaryOperation.LOGICAL_AND);
        result.Left.Should().BeOfType<UnaryOperationNode>().Which.Value.Should().BeSameAs(C1);
        result.Right.Should().BeOfType<UnaryOperationNode>().Which.Value.Should().BeSameAs(C2);
    }

    [Fact]
    public void NegatedConditionDistributesThroughNesting() {
        // Arrange
        BinaryOperationNode inner = new(DataType.BOOL, C1, BinaryOperation.LOGICAL_OR, C2);
        BinaryOperationNode outer = new(DataType.BOOL, inner, BinaryOperation.LOGICAL_AND, C1);

        // Act
        ValueNode negated = CodeGenerationAst.NegatedCondition(outer);

        // Assert
        BinaryOperationNode result = negated.Should().BeOfType<BinaryOperationNode>().Which;
        result.BinaryOperation.Should().Be(BinaryOperation.LOGICAL_OR);
        result.Left.Should().BeOfType<BinaryOperationNode>().Which.BinaryOperation.Should().Be(BinaryOperation.LOGICAL_AND);
        result.Right.Should().BeOfType<UnaryOperationNode>().Which.Value.Should().BeSameAs(C1);
    }
}
