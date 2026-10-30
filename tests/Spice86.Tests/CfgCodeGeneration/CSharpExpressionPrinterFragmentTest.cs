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
    public void SubtractionFromZeroIsANegation() {
        ConstantNode zero = new(DataType.UINT16, 0);
        RegisterNode ax = new(DataType.UINT16, (int)RegisterIndex.AxIndex);
        BinaryOperationNode subtract = new(DataType.UINT16, zero, BinaryOperation.MINUS, ax);

        CSharpFragment fragment = _printer.Print(subtract);

        fragment.Text.Should().Be("-AX");
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

    [Fact]
    public void ASignedRoundTripConversionIsTheIdentity() {
        // Arrange
        RegisterNode bx = new(DataType.UINT16, (int)RegisterIndex.BxIndex);
        TypeConversionNode roundTrip = new(DataType.UINT16,
            new TypeConversionNode(DataType.INT16, bx));
        RegisterNode dl = new(DataType.UINT8, 2);
        TypeConversionNode signExtension = new(DataType.UINT16,
            new TypeConversionNode(DataType.INT16,
                new TypeConversionNode(DataType.INT8, dl)));

        // Act
        CSharpFragment roundTripFragment = _printer.Print(roundTrip);
        CSharpFragment signExtensionFragment = _printer.Print(signExtension);

        // Assert
        roundTripFragment.Text.Should().Be("BX");
        signExtensionFragment.Text.Should().Be("(ushort)(short)(sbyte)DL");
    }



    [Fact]
    public void APromotedShiftOperandIsTruncatedBack() {
        // Arrange
        RegisterNode bx = new(DataType.UINT16, (int)RegisterIndex.BxIndex);
        BinaryOperationNode sum = new(DataType.UINT16, bx, BinaryOperation.PLUS,
            new ConstantNode(DataType.UINT16, 1));
        BinaryOperationNode shift = new(DataType.UINT16, sum, BinaryOperation.RIGHT_SHIFT,
            new ConstantNode(DataType.UINT16, 1));

        // Act
        CSharpFragment fragment = _printer.Print(shift);

        // Assert
        fragment.Text.Should().Be("(ushort)(BX + 1) >> 1");
    }

    [Fact]
    public void APromotedComparisonOperandIsTruncatedBack() {
        // Arrange
        RegisterNode bx = new(DataType.UINT16, (int)RegisterIndex.BxIndex);
        BinaryOperationNode sum = new(DataType.UINT16, bx, BinaryOperation.PLUS,
            new ConstantNode(DataType.UINT16, 1));
        BinaryOperationNode equal = new(DataType.BOOL, sum, BinaryOperation.EQUAL,
            new ConstantNode(DataType.UINT16, 0));

        // Act
        CSharpFragment fragment = _printer.Print(equal);

        // Assert
        fragment.Text.Should().Be("(ushort)(BX + 1) == 0");
    }

    [Fact]
    public void APromotedAdditiveOperandIsNotTruncated() {
        // Arrange
        RegisterNode bx = new(DataType.UINT16, (int)RegisterIndex.BxIndex);
        BinaryOperationNode inner = new(DataType.UINT16, bx, BinaryOperation.PLUS,
            new ConstantNode(DataType.UINT16, 1));
        BinaryOperationNode outer = new(DataType.UINT16, inner, BinaryOperation.PLUS,
            new ConstantNode(DataType.UINT16, 2));

        // Act
        CSharpFragment fragment = _printer.Print(outer);

        // Assert
        fragment.Text.Should().Be("BX + 1 + 2");
    }



    [Fact]
    public void AMaskedGrowthComparisonOperandIsNotTruncated() {
        // Arrange
        RegisterNode dx = new(DataType.UINT16, (int)RegisterIndex.DxIndex);
        BinaryOperationNode sum = new(DataType.UINT16, dx, BinaryOperation.PLUS,
            new ConstantNode(DataType.UINT16, 1));
        BinaryOperationNode mask = new(DataType.UINT16, sum, BinaryOperation.BITWISE_AND,
            new ConstantNode(DataType.UINT16, 0xFF));
        BinaryOperationNode equal = new(DataType.BOOL, mask, BinaryOperation.EQUAL,
            new ConstantNode(DataType.UINT16, 0));

        // Act
        CSharpFragment fragment = _printer.Print(equal);

        // Assert
        fragment.Text.Should().Be("(DX + 1 & 0x00FF) == 0");
    }

    [Fact]
    public void AnUnboundedOrShiftOperandIsTruncatedBack() {
        // Arrange
        RegisterNode dx = new(DataType.UINT16, (int)RegisterIndex.DxIndex);
        BinaryOperationNode sum = new(DataType.UINT16, dx, BinaryOperation.PLUS,
            new ConstantNode(DataType.UINT16, 1));
        BinaryOperationNode or = new(DataType.UINT16, sum, BinaryOperation.BITWISE_OR,
            new ConstantNode(DataType.UINT16, 1));
        BinaryOperationNode shift = new(DataType.UINT16, or, BinaryOperation.RIGHT_SHIFT,
            new ConstantNode(DataType.UINT16, 1));

        // Act
        CSharpFragment fragment = _printer.Print(shift);

        // Assert
        fragment.Text.Should().Be("(ushort)(DX + 1 | 1) >> 1");
    }

    [Fact]
    public void AComplementComparisonOperandIsTruncatedBack() {
        // Arrange
        RegisterNode ax = new(DataType.UINT16, (int)RegisterIndex.AxIndex);
        UnaryOperationNode complement = new(DataType.UINT16, UnaryOperation.BITWISE_NOT, ax);
        BinaryOperationNode equal = new(DataType.BOOL, complement, BinaryOperation.EQUAL,
            new ConstantNode(DataType.UINT16, 0));

        // Act
        CSharpFragment fragment = _printer.Print(equal);

        // Assert
        fragment.Text.Should().Be("(ushort)~AX == 0");
    }

    [Fact]
    public void TopByteConstantKeepsTheAddition() {
        // 0x80 is its own negation at 8 bits: no subtraction magnitude exists for it.
        RegisterNode al = new(DataType.UINT8, (int)RegisterIndex.AxIndex);
        BinaryOperationNode sum = new(DataType.UINT8, al, BinaryOperation.PLUS,
            new ConstantNode(DataType.UINT8, 0x80));

        CSharpFragment fragment = _printer.Print(sum);

        fragment.Text.Should().Be("AL + 0x80");
    }

    [Fact]
    public void NarrowTopConstantInAWiderAdditionKeepsTheAddition() {
        // An unsigned 16-bit 0xFFFE in a 32-bit addition is +0xFFFE, not -2.
        RegisterNode ecx = new(DataType.UINT32, (int)RegisterIndex.CxIndex);
        BinaryOperationNode sum = new(DataType.UINT32, ecx, BinaryOperation.PLUS,
            new ConstantNode(DataType.UINT16, 0xFFFE));

        CSharpFragment fragment = _printer.Print(sum);

        fragment.Text.Should().Be("ECX + 0xFFFE");
    }



    [Fact]
    public void LargeNegativeSignedConstantPrintsASubtraction() {
        // A signed constant sign-extends to the operation width: any magnitude is a subtraction.
        RegisterNode ax = new(DataType.UINT16, (int)RegisterIndex.AxIndex);
        BinaryOperationNode sum = new(DataType.UINT16, ax, BinaryOperation.PLUS,
            new ConstantNode(DataType.INT16, unchecked((ushort)-0x01C4)));

        CSharpFragment fragment = _printer.Print(sum);

        fragment.Text.Should().Be("AX - 0x01C4");
    }

    private CSharpFragment StackWord(ValueNode offset) {
        SegmentedPointerNode pointer = new(DataType.UINT16, new SegmentRegisterNode(2), null, offset);
        return _printer.Print(pointer);
    }
}
