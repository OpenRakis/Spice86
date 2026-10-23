namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Xunit;

public class NegatedConditionTest {
    private static readonly RegisterNode Ax = new(DataType.UINT16, 0);
    private static readonly RegisterNode Bx = new(DataType.UINT16, 3);

    [Theory]
    [InlineData(BinaryOperation.EQUAL, "AX != BX")]
    [InlineData(BinaryOperation.NOT_EQUAL, "AX == BX")]
    [InlineData(BinaryOperation.LESS_THAN, "AX >= BX")]
    [InlineData(BinaryOperation.GREATER_THAN_OR_EQUAL, "AX < BX")]
    [InlineData(BinaryOperation.GREATER_THAN, "AX <= BX")]
    [InlineData(BinaryOperation.LESS_THAN_OR_EQUAL, "AX > BX")]
    public void ComparisonIsInverted(BinaryOperation operation, string expected) {
        // Act
        ValueNode negated = InstructionLowerer.NegatedCondition(new BinaryOperationNode(DataType.BOOL, Ax, operation, Bx));

        // Assert
        Print(negated).Should().Be(expected);
    }

    [Fact]
    public void NotIsUnwrappedAndOtherConditionsAreWrapped() {
        // Arrange
        CpuFlagNode zero = new(Flags.Zero);
        BinaryOperationNode carryOrZero = new(DataType.BOOL, new CpuFlagNode(Flags.Carry), BinaryOperation.LOGICAL_OR, zero);

        // Act
        ValueNode unwrapped = InstructionLowerer.NegatedCondition(new UnaryOperationNode(DataType.BOOL, UnaryOperation.NOT, zero));
        ValueNode wrapped = InstructionLowerer.NegatedCondition(carryOrZero);

        // Assert
        Print(unwrapped).Should().Be("ZeroFlag");
        Print(wrapped).Should().Be("!(CarryFlag || ZeroFlag)");
    }

    private static string Print(ValueNode node) =>
        new CSharpExpressionPrinter(new Dictionary<ushort, string>()).Print(node).Text;
}
