namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Xunit;

public class CodeGenerationAstTest {
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
}
