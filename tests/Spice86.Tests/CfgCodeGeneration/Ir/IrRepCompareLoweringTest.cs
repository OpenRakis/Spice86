namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Tests.CfgCpu;

using Xunit;

using static Spice86.Tests.CfgCodeGeneration.Ir.IrTestPrograms;

/// <summary>
/// The IR rep-compare lowering on real parsed instructions: a <c>repe cmpsb</c> whose comparison
/// flags feed only the loop latch declares an <c>equal_</c> temporary the latch negates; one whose
/// flags a later jump reads keeps its <c>AluN.Sub</c> call.
/// </summary>
public sealed class IrRepCompareLoweringTest {
    private static bool IsEqualDeclaration(IVisitableAstNode node) =>
        node is VariableDeclarationNode declaration
        && declaration.VariableName.StartsWith("equal_");

    private static bool IsNegatedVariable(IVisitableAstNode node) =>
        node is UnaryOperationNode {
            UnaryOperation: UnaryOperation.NOT,
            Value: VariableReferenceNode
        };

    private static bool IsAluNode(IVisitableAstNode node) =>
        node is AluStatementNode or AluOperationNode;

    private static IrMethod RepCompareLowered(params CfgBlock[] blocks) {
        IrMethod method = Lift(blocks);
        IrPassPipeline.Run(method, IrPass.RepCompareLowering, verify: true);
        return method;
    }

    [Fact]
    public void RepeCmpsbWithFlagsFeedingOnlyTheLatchUsesEqual() {
        // Arrange: repe cmpsb / nop
        TestInstructionHelper helper = new();
        CfgInstruction repeCmpsb = Parse(helper, 0x0000, 0xF3, 0xA6);
        CfgInstruction nop = Parse(helper, 0x0002, 0x90);
        Chain(repeCmpsb, nop);
        CfgBlock block = Block(1, repeCmpsb, nop);

        // Act
        IrMethod method = RepCompareLowered(block);

        // Assert
        Nodes(method).Should().Contain(node => IsEqualDeclaration(node));
        Nodes(method).Should().Contain(node => IsNegatedVariable(node),
            "the latch negates the equality temporary");
        Nodes(method).Should().NotContain(node => IsAluNode(node));
    }

    [Fact]
    public void RepeCmpsbWithLiveFlagsKeepsTheSub() {
        // Arrange: repe cmpsb / jz +2 - the jz reads the flags the compare writes
        TestInstructionHelper helper = new();
        CfgInstruction repeCmpsb = Parse(helper, 0x0000, 0xF3, 0xA6);
        CfgInstruction jz = Parse(helper, 0x0002, 0x74, 0x02);
        CfgInstruction fallthrough = Parse(helper, 0x0004, 0x90);
        CfgInstruction taken = Parse(helper, 0x0006, 0x90);
        Chain(repeCmpsb, jz);
        CfgBlock repeBlock = Block(1, repeCmpsb, jz);
        Branch(jz, fallthrough, taken);

        // Act
        IrMethod method = RepCompareLowered(repeBlock, Block(2, fallthrough), Block(3, taken));

        // Assert
        Nodes(method).Should().NotContain(node => IsEqualDeclaration(node));
        Nodes(method).Should().Contain(node => IsAluNode(node));
    }
}
