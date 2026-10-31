namespace Spice86.Tests.CfgCpu.Ast;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Shared.Emulator.Memory;

using Xunit;

/// <summary>
/// <see cref="AstBuilder.DirectionStep"/> builds the <c>DirectionFlag ? -N : N</c> select.
/// </summary>
public sealed class AstBuilderDirectionStepTest {
    [Theory]
    [InlineData(BitWidth.BYTE_8, -1, 1)]
    [InlineData(BitWidth.WORD_16, -2, 2)]
    [InlineData(BitWidth.DWORD_32, -4, 4)]
    public void DirectionStepBuildsTheSelectOfTheWidth(BitWidth width, int backward, int forward) {
        // Act
        ConditionalValueNode step = AstBuilder.DirectionStep(width);

        // Assert
        step.DataType.Should().Be(DataType.INT16);
        step.Condition.Should().BeEquivalentTo(new CpuFlagNode(Flags.Direction));
        step.WhenTrue.Should().BeEquivalentTo(
            new ConstantNode(DataType.INT16, (ulong)(ushort)backward));
        step.WhenFalse.Should().BeEquivalentTo(new ConstantNode(DataType.INT16, (ulong)forward));
    }
}
