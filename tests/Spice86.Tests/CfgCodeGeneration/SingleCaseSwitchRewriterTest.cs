namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Shared.Emulator.Memory;

using Xunit;

public class SingleCaseSwitchRewriterTest {
    private static readonly CpuFlagNode Zero = new(Flags.Zero);
    private static readonly RegisterNode Si = new(DataType.UINT16, (int)RegisterIndex.SiIndex);

    private static SwitchNode SingleCaseSwitch(IVisitableAstNode caseBody, IVisitableAstNode defaultBody) =>
        new(Si, [new SwitchSection(0x0012, CodeGenerationAst.Sequence(caseBody))],
            CodeGenerationAst.Sequence(defaultBody));

    private static FailAsUntestedNode Failure() =>
        new([UntestedMessagePart.Literal("Untested near jump target")]);

    [Fact]
    public void BreakNestedInAnIfKeepsTheSwitch() {
        // Arrange
        BlockNode caseBody = new(
            new IfElseNode(Zero, new BlockNode(new BreakNode()), new BlockNode()),
            new BreakNode());
        BlockNode body = new(SingleCaseSwitch(caseBody, Failure()));

        // Act
        BlockNode result = SingleCaseSwitchRewriter.Apply(body);

        // Assert
        result.Statements.Should().ContainSingle().Which.Should().BeOfType<SwitchNode>();
    }
}
