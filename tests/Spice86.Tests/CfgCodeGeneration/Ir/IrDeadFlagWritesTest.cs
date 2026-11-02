namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Tests.CfgCpu;

using Xunit;

using static Spice86.Tests.CfgCodeGeneration.Ir.IrTestPrograms;

/// <summary>
/// The IR dead flag write removal on real parsed instructions: a plain flag write nobody reads
/// disappears, the same write before a flag-reading jump stays, and a non-plain write behind an
/// after-check is never touched.
/// </summary>
public sealed class IrDeadFlagWritesTest {
    private static IrMethod DeadFlagWritten(params CfgBlock[] blocks) {
        IrMethod method = Lift(blocks);
        IrPassPipeline.Run(method, IrPass.DeadFlagWrites, verify: true);
        return method;
    }

    private static bool WritesFlagRegister(IVisitableAstNode node) =>
        node is BinaryOperationNode {
            BinaryOperation: BinaryOperation.ASSIGN,
            Left: FlagRegisterNode
        };

    private static bool WritesCarryFlag(IVisitableAstNode node) =>
        node is BinaryOperationNode {
            BinaryOperation: BinaryOperation.ASSIGN,
            Left: CpuFlagNode { FlagMask: Flags.Carry }
        };

    [Fact]
    public void ClcWithDeadCarryIsRemoved() {
        // Arrange: clc / nop
        TestInstructionHelper helper = new();
        CfgInstruction clc = Parse(helper, 0x0000, 0xF8);
        CfgInstruction nop = Parse(helper, 0x0001, 0x90);
        Chain(clc, nop);
        CfgBlock block = Block(1, clc, nop);

        // Act
        IrMethod method = DeadFlagWritten(block);

        // Assert
        Nodes(method).Should().NotContain(node => node.GetType() == typeof(CpuFlagNode));
    }

    [Fact]
    public void ClcBeforeJcKeepsTheWrite() {
        // Arrange: clc / jc +2
        TestInstructionHelper helper = new();
        CfgInstruction clc = Parse(helper, 0x0000, 0xF8);
        CfgInstruction jc = Parse(helper, 0x0001, 0x72, 0x02);
        CfgInstruction fallthrough = Parse(helper, 0x0003, 0x90);
        CfgInstruction taken = Parse(helper, 0x0005, 0x90);
        Chain(clc, jc);
        CfgBlock clcBlock = Block(1, clc, jc);
        Branch(jc, fallthrough, taken);

        // Act
        IrMethod method = DeadFlagWritten(clcBlock, Block(2, fallthrough), Block(3, taken));

        // Assert
        Nodes(method).Should().Contain(node => WritesCarryFlag(node));
    }

    [Fact]
    public void PopfKeepsItsFlagRegisterWrite() {
        // Arrange: popf / nop - the write is not a plain value and the instruction carries an
        // after-check
        TestInstructionHelper helper = new();
        CfgInstruction popf = Parse(helper, 0x0000, 0x9D);
        CfgInstruction nop = Parse(helper, 0x0001, 0x90);
        CfgBlock popfBlock = Block(1, popf);
        CfgBlock nopBlock = Block(2, nop);

        // Act
        IrMethod method = DeadFlagWritten(popfBlock, nopBlock);

        // Assert
        Nodes(method).Should().Contain(node => WritesFlagRegister(node));
    }
}
