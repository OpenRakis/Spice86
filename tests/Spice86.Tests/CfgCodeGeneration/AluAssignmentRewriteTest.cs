namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Tests.CfgCpu;

using Xunit;

using static Spice86.Tests.CfgCodeGeneration.Ir.IrTestPrograms;

/// <summary>
/// The shared ALU assignment rewrite on real parsed instructions: a declaration or conversion over an
/// ALU call becomes plain code when the call's flags are dead, and keeps the call when the cleaned
/// expansion still needs its temps or its flag write reads more than the result.
/// </summary>
public sealed class AluAssignmentRewriteTest {
    private static IrMethod LoweredAlu(params CfgBlock[] blocks) {
        IrMethod method = Lift(blocks);
        IrPassPipeline.Run(method, IrPass.AluLowering, verify: true);
        return method;
    }

    private static bool IsAluCall(IVisitableAstNode node) => node is AluOperationNode or AluStatementNode;

    private static bool DeclaresPlainAddition(IVisitableAstNode node) =>
        node is VariableDeclarationNode {
            Initializer: BinaryOperationNode { BinaryOperation: BinaryOperation.PLUS } };

    [Fact]
    public void XaddWithDeadFlags_DeclaresTheSumAsPlainAddition() {
        // Arrange: xadd ax, bx / nop - the sum declaration's call has no live flag
        TestInstructionHelper helper = new();
        CfgBlock block = Linear(helper,
            (0x0000, [0x0F, 0xC1, 0xD8]),
            (0x0003, [0x90]));

        // Act
        IrMethod method = LoweredAlu(block);

        // Assert
        Nodes(method).Should().NotContain(node => IsAluCall(node));
        Nodes(method).Should().Contain(node => DeclaresPlainAddition(node));
    }

    [Fact]
    public void XaddWithLiveSignFlag_KeepsTheCall() {
        // Arrange: xadd ax, bx / js - the live flag reads the result temp, so the cleaned
        // expansion keeps two statements and the wrapped rewrite refuses
        TestInstructionHelper helper = new();
        CfgInstruction xadd = Parse(helper, 0x0000, 0x0F, 0xC1, 0xD8);
        CfgInstruction js = Parse(helper, 0x0003, 0x78, 0x02);
        CfgInstruction fallthrough = Parse(helper, 0x0005, 0x90);
        CfgInstruction taken = Parse(helper, 0x0007, 0x90);
        Chain(xadd, js);
        CfgBlock xaddBlock = Block(1, xadd, js);
        Branch(js, fallthrough, taken);

        // Act
        IrMethod method = LoweredAlu(xaddBlock, Block(2, fallthrough), Block(3, taken));

        // Assert
        Nodes(method).Should().Contain(node => IsAluCall(node));
    }

    [Fact]
    public void MulWithLiveCarry_KeepsTheCall() {
        // Arrange: mul cl / jc - the carry formula reads the result temp
        TestInstructionHelper helper = new();
        CfgInstruction mul = Parse(helper, 0x0000, 0xF6, 0xE1);
        CfgInstruction jc = Parse(helper, 0x0002, 0x72, 0x02);
        CfgInstruction fallthrough = Parse(helper, 0x0004, 0x90);
        CfgInstruction taken = Parse(helper, 0x0006, 0x90);
        Chain(mul, jc);
        CfgBlock mulBlock = Block(1, mul, jc);
        Branch(jc, fallthrough, taken);

        // Act
        IrMethod method = LoweredAlu(mulBlock, Block(2, fallthrough), Block(3, taken));

        // Assert
        Nodes(method).Should().Contain(node => IsAluCall(node));
    }

    [Fact]
    public void ImulWithImmediateAndLiveOverflow_KeepsTheCall() {
        // Arrange: imul ax, bx, 5 / jo - a conversion over the call whose live flag reads the
        // result temp
        TestInstructionHelper helper = new();
        CfgInstruction imul = Parse(helper, 0x0000, 0x6B, 0xC3, 0x05);
        CfgInstruction jo = Parse(helper, 0x0003, 0x70, 0x02);
        CfgInstruction fallthrough = Parse(helper, 0x0005, 0x90);
        CfgInstruction taken = Parse(helper, 0x0007, 0x90);
        Chain(imul, jo);
        CfgBlock imulBlock = Block(1, imul, jo);
        Branch(jo, fallthrough, taken);

        // Act
        IrMethod method = LoweredAlu(imulBlock, Block(2, fallthrough), Block(3, taken));

        // Assert
        Nodes(method).Should().Contain(node => IsAluCall(node));
    }

    [Fact]
    public void SubFromMemoryWithLiveCarry_KeepsTheCall() {
        // Arrange: sub ax, word [0] / jc - the memory operand is read by the result and the carry
        // formula, so the cleaned expansion keeps a statement the rewrite does not handle
        TestInstructionHelper helper = new();
        CfgInstruction add = Parse(helper, 0x0000, 0x2B, 0x06, 0x00, 0x00);
        CfgInstruction jc = Parse(helper, 0x0004, 0x72, 0x02);
        CfgInstruction fallthrough = Parse(helper, 0x0006, 0x90);
        CfgInstruction taken = Parse(helper, 0x0008, 0x90);
        Chain(add, jc);
        CfgBlock addBlock = Block(1, add, jc);
        Branch(jc, fallthrough, taken);

        // Act
        IrMethod method = LoweredAlu(addBlock, Block(2, fallthrough), Block(3, taken));

        // Assert
        Nodes(method).Should().Contain(node => IsAluCall(node));
    }

    [Fact]
    public void AdcWithLiveCarry_KeepsTheCall() {
        // Arrange: adc ax, bx / jc - the carry formula reads the carry flag itself, so the flag
        // write cannot be rebuilt from the destination
        TestInstructionHelper helper = new();
        CfgInstruction adc = Parse(helper, 0x0000, 0x11, 0xD8);
        CfgInstruction jc = Parse(helper, 0x0002, 0x72, 0x02);
        CfgInstruction fallthrough = Parse(helper, 0x0004, 0x90);
        CfgInstruction taken = Parse(helper, 0x0006, 0x90);
        Chain(adc, jc);
        CfgBlock adcBlock = Block(1, adc, jc);
        Branch(jc, fallthrough, taken);

        // Act
        IrMethod method = LoweredAlu(adcBlock, Block(2, fallthrough), Block(3, taken));

        // Assert
        Nodes(method).Should().Contain(node => IsAluCall(node));
    }
}
