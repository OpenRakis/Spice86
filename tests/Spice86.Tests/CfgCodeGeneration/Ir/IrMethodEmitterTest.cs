namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Microsoft.CodeAnalysis;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

using Spice86.Tests.CfgCpu;

using Xunit;

using static Spice86.Tests.CfgCodeGeneration.Ir.IrTestPrograms;

/// <summary>
/// The IR pipeline emits whole methods on small lifted programs: an <c>aam</c> throw-if nests as a
/// guard, and every emitted source compiles.
/// </summary>
public sealed class IrMethodEmitterTest {





    [Fact]
    public void AamThrowIfDoesNotNestTheRestOfTheMethod() {
        // Arrange
        TestInstructionHelper helper = new();
        CfgInstruction aam = Parse(helper, 0x0000, 0xD4, 0x0A);
        CfgInstruction next = Parse(helper, 0x0002, 0x90);
        aam.Successors.Add(next);
        aam.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { next };
        CfgBlock block = Block(1, aam, next);

        // Act
        string source = Generate(SingleEntry(block), block);

        // Assert
        AssertCompiles(source);
        source.Should().Contain("if (v2_0000 == 0)");
        source.Should().Contain("        // F000:0002");
    }

    [Fact]
    public void UniqueLabel_KeepsAFreeLabel() {
        // Arrange
        HashSet<string> taken = ["other"];

        // Act
        string label = IrMethodEmitter.UniqueLabel("L_0040_0", taken);

        // Assert
        label.Should().Be("L_0040_0");
        taken.Should().Contain("L_0040_0");
    }

    [Fact]
    public void UniqueLabel_SuffixesACollision() {
        // Arrange
        HashSet<string> taken = ["L_0040_0"];

        // Act
        string label = IrMethodEmitter.UniqueLabel("L_0040_0", taken);

        // Assert
        label.Should().Be("L_0040_0_ir");
        taken.Should().Contain("L_0040_0_ir");
    }

    [Fact]
    public void UniqueLabel_NumbersTheSuffixWhenItCollidesToo() {
        // Arrange
        HashSet<string> taken = ["L_0040_0", "L_0040_0_ir"];

        // Act
        string label = IrMethodEmitter.UniqueLabel("L_0040_0", taken);

        // Assert
        label.Should().Be("L_0040_0_ir2");
        taken.Should().Contain("L_0040_0_ir2");
    }


}
