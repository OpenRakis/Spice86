namespace Spice86.Tests.CfgCpu.Ast;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;

using Xunit;

public class RepeatedStringAstTest {
    [Theory]
    [InlineData(new byte[] { 0xF3, 0xA6 }, false)]
    [InlineData(new byte[] { 0xF2, 0xAE }, true)]
    public void RepeAndRepneLoopsExitOnTheZeroFlagAfterEachIteration(byte[] bytes, bool exitZeroFlag) {
        // Act
        IVisitableAstNode ast = ParsedExecutionAst.Of(bytes);

        // Assert
        BlockNode block = ast.Should().BeOfType<BlockNode>().Subject;
        block.Statements.Should().HaveCount(2);
        block.Statements[1].Should().BeOfType<MoveIpNextNode>();
        WhileWithExitNode loop = block.Statements[0].Should().BeOfType<WhileWithExitNode>().Subject;
        loop.Body.Statements.Should().HaveCount(2);
        loop.ExitCondition.Should().Be(new BinaryOperationNode(DataType.BOOL, new CpuFlagNode(Flags.Zero),
            BinaryOperation.EQUAL, new ConstantNode(DataType.BOOL, exitZeroFlag ? 1UL : 0UL)));
    }

    [Theory]
    [InlineData(new byte[] { 0xF3, 0xA4 })]
    [InlineData(new byte[] { 0xF3, 0xAB })]
    public void RepWithoutFlagConditionKeepsAPlainWhile(byte[] bytes) {
        // Act
        IVisitableAstNode ast = ParsedExecutionAst.Of(bytes);

        // Assert
        BlockNode block = ast.Should().BeOfType<BlockNode>().Subject;
        block.Statements[0].Should().BeOfType<WhileNode>();
    }
}
