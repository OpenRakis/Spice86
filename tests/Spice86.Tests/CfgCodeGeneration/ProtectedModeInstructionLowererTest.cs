namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

using System.Collections.Immutable;

using Xunit;

public class ProtectedModeInstructionLowererTest {
    [Fact]
    public void AlwaysFaultingFarJumpStillValidatesAndLoadsCsBeforeFailingAsUntested() {
        // Arrange
        CfgInstruction instruction = Instruction();
        InstructionLowerer lowerer = Lowerer();
        JumpFarNode jump = new(instruction, Address(0x0018, 0x0010));

        // Act
        BlockNode lowered = lowerer.LowerForIr(instruction, jump, false);
        string source = Print(IrNodeTyper.TypeStatement(lowered));

        // Assert
        source.Should().Contain("ValidateFarCodeSegmentTransfer(0x0018);");
        source.Should().Contain("LoadSegmentRegister(1, 0x0018);");
        source.Should().Contain("throw FailAsUntested(\"Untested jump at ");
        source.IndexOf("ValidateFarCodeSegmentTransfer", StringComparison.Ordinal)
            .Should().BeLessThan(source.IndexOf("LoadSegmentRegister", StringComparison.Ordinal));
        source.IndexOf("LoadSegmentRegister", StringComparison.Ordinal)
            .Should().BeLessThan(source.IndexOf("throw FailAsUntested", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(BitWidth.WORD_16, "FarCall")]
    [InlineData(BitWidth.DWORD_32, "FarCall32")]
    public void GateSelectorWithoutADirectTargetNeedsNoSegmentVariable(BitWidth width, string helper) {
        // Arrange
        CfgInstruction instruction = Instruction();
        InstructionLowerer lowerer = Lowerer();
        CallFarNode call = new(instruction, Address(0x0028, 0), width);

        // Act
        BlockNode lowered = lowerer.LowerForIr(instruction, call, false);
        string source = Print(IrNodeTyper.TypeStatement(lowered));

        // Assert
        source.Should().Contain($"{helper}(cs1, 0x0001, 0x0028, _ => throw FailAsUntested(");
        source.Should().Contain("unexpectedly resolved to a direct target");
        source.Should().Contain("Untested return from call");
    }

    [Theory]
    [InlineData(BitWidth.WORD_16, "InterruptRet")]
    [InlineData(BitWidth.DWORD_32, "InterruptRet32")]
    public void InterruptReturnPreservesItsOperandWidth(BitWidth width, string helper) {
        // Arrange
        CfgInstruction instruction = Instruction();
        InstructionLowerer lowerer = Lowerer();
        ReturnInterruptNode interruptReturn = new(instruction, width);

        // Act
        BlockNode lowered = lowerer.LowerForIr(instruction, interruptReturn, false);
        string source = Print(IrNodeTyper.TypeStatement(lowered));

        // Assert
        source.Trim().Should().Be($"return {helper}();");
    }

    private static InstructionLowerer Lowerer() {
        CfgPartitionedProgram program = new() { Partitions = [], Transfers = [] };
        CfgGeneratorContext context = new(program, [], [], [], new Dictionary<ushort, string> {
            [0x1000] = "cs1"
        }, [], [], []);
        return new InstructionLowerer(context, new TransferEmitter(context));
    }

    private static CfgInstruction Instruction() {
        InstructionField<ushort> opcode = new(0, 1, 0x10000, 0xEA, ImmutableList.Create<byte?>(0xEA), true);
        return new CfgInstruction(0, new SegmentedAddress(0x1000, 0), opcode, [], null);
    }

    private static SegmentedAddressNode Address(ushort segment, ushort offset) =>
        new(new ConstantNode(DataType.UINT16, segment), new ConstantNode(DataType.UINT16, offset));

    private static string Print(IVisitableAstNode node) {
        CSharpSourceWriter writer = new();
        CSharpExpressionPrinter expressions = new(new Dictionary<ushort, string> { [0x1000] = "cs1" });
        CSharpStatementPrinter printer = new(writer, expressions,
            _ => throw new InvalidOperationException("No direct target was observed."));
        printer.Print(node);
        return writer.ToString();
    }
}
