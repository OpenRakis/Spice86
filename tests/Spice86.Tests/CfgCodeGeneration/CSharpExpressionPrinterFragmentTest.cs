namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;

using Xunit;

/// <summary>
/// Locks in the behavior-preserving contract of <see cref="CSharpExpressionPrinter"/>: the fragment it
/// returns for an expression-shaped node must render exactly the expected text for representative AST nodes
/// (register, memory, binary op, constant). The fragment is a transparent carrier of the rendered text.
/// </summary>
public class CSharpExpressionPrinterFragmentTest {
    private readonly CSharpExpressionPrinter _printer = new(new Dictionary<ushort, string>());

    [Fact]
    public void NegateOfNegativeConstantIsParenthesized() {
        ConstantNode inner = new(DataType.INT16, 0xFFFE); // -2
        UnaryOperationNode negate = new(DataType.INT16, UnaryOperation.NEGATE, inner);

        CSharpFragment fragment = _printer.Print(negate);

        fragment.Text.Should().Be("-(-2)");
    }

    [Fact]
    public void FragmentTextEqualsToString() {
        ConstantNode constant = new(DataType.UINT16, 0x2A);

        CSharpFragment fragment = _printer.Print(constant);

        fragment.ToString().Should().Be(fragment.Text);
    }

    [Fact]
    public void FlagNotEqualToTrueRendersNegatedFlag() {
        BinaryOperationNode comparison = new(DataType.BOOL, new CpuFlagNode(Flags.Zero), BinaryOperation.NOT_EQUAL, new ConstantNode(DataType.BOOL, 1));

        CSharpFragment fragment = _printer.Print(comparison);

        fragment.Text.Should().Be("!ZeroFlag");
    }

    [Fact]
    public void TrueEqualToFlagRendersBareFlag() {
        BinaryOperationNode comparison = new(DataType.BOOL, new ConstantNode(DataType.BOOL, 1), BinaryOperation.EQUAL, new CpuFlagNode(Flags.Zero));

        CSharpFragment fragment = _printer.Print(comparison);

        fragment.Text.Should().Be("ZeroFlag");
    }

    [Fact]
    public void ByteMinValueDisplacementRendersUnsignedMagnitude() {
        BinaryOperationNode offset = new(DataType.UINT16, new RegisterNode(DataType.UINT16, 5), BinaryOperation.PLUS, new ConstantNode(DataType.INT8, 0x80));

        CSharpFragment fragment = StackWord(offset);

        fragment.Text.Should().Be("UInt16[SS, (ushort)(BP - 0x80)]");
    }

    [Fact]
    public void Int32MinValueDisplacementKeepsAddition() {
        BinaryOperationNode sum = new(DataType.UINT32, new RegisterNode(DataType.UINT32, 3), BinaryOperation.PLUS, new ConstantNode(DataType.INT32, 0x80000000));

        CSharpFragment fragment = _printer.Print(sum);

        fragment.Text.Should().Be("EBX + int.MinValue");
    }

    [Fact]
    public void ExplicitCastKeepsAConstantUnfolded() {
        ExplicitCastNode cast = new(DataType.UINT8, new ConstantNode(DataType.UINT16, 0x21));

        CSharpFragment fragment = _printer.Print(cast);

        fragment.Text.Should().Be("(byte)0x0021");
    }

    [Fact]
    public void BoolConvertedToANumberIsAConditional() {
        // Arrange
        TypeConversionNode conversion = new(DataType.UINT16, new CpuFlagNode((ushort)StatusFlags.Carry));
        RegisterNode ax = new(DataType.UINT16, (int)RegisterIndex.AxIndex);
        BinaryOperationNode sum = new(DataType.UINT16, ax, BinaryOperation.PLUS, conversion);

        // Act
        CSharpFragment conversionFragment = _printer.Print(conversion);
        CSharpFragment sumFragment = _printer.Print(sum);

        // Assert
        conversionFragment.Text.Should().Be("(CarryFlag ? 1 : 0)");
        sumFragment.Text.Should().Be("AX + (CarryFlag ? 1 : 0)");
    }

    private CSharpFragment StackWord(ValueNode offset) {
        SegmentedPointerNode pointer = new(DataType.UINT16, new SegmentRegisterNode(2), null, offset);
        return _printer.Print(pointer);
    }
}
