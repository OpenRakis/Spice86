namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Tests.CfgCpu.Ast;

using Xunit;

public class RepCompareLoopPatternTest {
    [Fact]
    public void RepeCmpsComparesBothStringsAndStopsOnADifference() {
        // Act
        RepCompareLoopPattern compare = RepCompareLoopPattern.TryMatch(ParsedExecutionAst.Of(0xF3, 0xA6))
            ?? throw new InvalidOperationException("expected RepCompareLoopPattern for REPE CMPSB");

        // Assert
        Print(compare.CounterCondition).Should().Be("CX != 0");
        Print(compare.Left).Should().Be("UInt8[DS, SI]");
        Print(compare.Right).Should().Be("UInt8[ES, DI]");
        compare.Updates.Should().HaveCount(3);
        Print(compare.Updates[2]).Should().Be("CX = (ushort)(CX - 1)");
        compare.StopWhenEqual.Should().BeFalse();
    }

    [Fact]
    public void RepneScasComparesTheAccumulatorAndStopsOnAMatch() {
        // Act
        RepCompareLoopPattern compare = RepCompareLoopPattern.TryMatch(ParsedExecutionAst.Of(0xF2, 0xAE))
            ?? throw new InvalidOperationException("expected RepCompareLoopPattern for REPNE SCASB");

        // Assert
        Print(compare.Left).Should().Be("AL");
        Print(compare.Right).Should().Be("UInt8[ES, DI]");
        compare.Updates.Should().HaveCount(2);
        compare.StopWhenEqual.Should().BeTrue();
    }

    [Fact]
    public void WordCompareReadsWords() {
        // Act
        RepCompareLoopPattern compare = RepCompareLoopPattern.TryMatch(ParsedExecutionAst.Of(0xF3, 0xA7))
            ?? throw new InvalidOperationException("expected RepCompareLoopPattern for REPE CMPSW");

        // Assert
        Print(compare.Left).Should().Be("UInt16[DS, SI]");
        Print(compare.Right).Should().Be("UInt16[ES, DI]");
    }

    [Theory]
    [InlineData(new byte[] { 0xF3, 0xA4 })]
    [InlineData(new byte[] { 0xA6 })]
    [InlineData(new byte[] { 0x39, 0xD8 })]
    public void OtherInstructionsAreNotMatched(byte[] bytes) {
        // Act
        RepCompareLoopPattern? compare = RepCompareLoopPattern.TryMatch(ParsedExecutionAst.Of(bytes));

        // Assert
        compare.Should().BeNull();
    }

    [Fact]
    public void LoopsThatAreNotACompareAreNotMatched() {
        // Arrange
        BlockNode parsed = (BlockNode)ParsedExecutionAst.Of(0xF3, 0xA6);
        WhileWithExitNode loop = (WhileWithExitNode)parsed.Statements[0];
        BlockNode notASubtraction = new(loop with { Body = new BlockNode(new BlockNode(), loop.Body.Statements[1]) }, parsed.Statements[1]);
        BlockNode notAZeroFlagExit = new(loop with { ExitCondition = new CpuFlagNode(Flags.Carry) }, parsed.Statements[1]);

        // Act
        RepCompareLoopPattern? first = RepCompareLoopPattern.TryMatch(notASubtraction);
        RepCompareLoopPattern? second = RepCompareLoopPattern.TryMatch(notAZeroFlagExit);

        // Assert
        first.Should().BeNull();
        second.Should().BeNull();
    }

    private static string Print(IVisitableAstNode node) =>
        new CSharpExpressionPrinter(new Dictionary<ushort, string>()).Print((ValueNode)node).Text;
}
