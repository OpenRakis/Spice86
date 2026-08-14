namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;

using System.Reflection;

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

        fragment.Text.Should().Be("SsUInt16[(ushort)(BP - 0x80)]");
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
    public void StackAddressKeepsIts32BitWidth() {
        // Arrange
        BinaryOperationNode offset = new(DataType.UINT32,
            new RegisterNode(DataType.UINT32, (int)RegisterIndex.SpIndex),
            BinaryOperation.PLUS, new ConstantNode(DataType.UINT32, 4));

        // Act
        CSharpFragment fragment = StackWord(offset);

        // Assert
        fragment.Text.Should().Be("UInt16[SS, (uint)(ESP + 4), SegmentAccessKind.Stack]");
    }

    [Fact]
    public void StackSegmentPointerUsesStackAccessValidationAtTheAddressLimit() {
        // Arrange
        SegmentedPointerNode pointer = new(DataType.UINT32,
            new SegmentRegisterNode((int)SegmentRegisterIndex.SsIndex), null,
            new ConstantNode(DataType.UINT32, uint.MaxValue));

        // Act
        CSharpFragment fragment = _printer.Print(pointer);

        // Assert
        fragment.Text.Should().Be("UInt32[SS, 0xFFFFFFFFu, SegmentAccessKind.Stack]");
    }

    [Fact]
    public void ExtendedMemoryOffsetKeepsDwordAddressWidth() {
        // Arrange
        BinaryOperationNode offset = new(DataType.UINT32, new ConstantNode(DataType.UINT32, 0),
            BinaryOperation.PLUS, new ConstantNode(DataType.UINT32, 0x500000));

        // Act
        CSharpFragment fragment = StackWord(offset);

        // Assert
        fragment.Text.Should().Be("UInt16[SS, (uint)(0 + 0x00500000), SegmentAccessKind.Stack]");
    }

    [Theory]
    [InlineData(BinaryOperation.LEFT_SHIFT, "EAX << (int)ECX")]
    [InlineData(BinaryOperation.RIGHT_SHIFT, "EAX >> (int)ECX")]
    public void DwordShiftCountIsConvertedToInt(BinaryOperation operation, string expected) {
        // Arrange
        BinaryOperationNode shift = new(DataType.UINT32,
            new RegisterNode(DataType.UINT32, (int)RegisterIndex.AxIndex), operation,
            new RegisterNode(DataType.UINT32, (int)RegisterIndex.CxIndex));

        // Act
        CSharpFragment fragment = _printer.Print(shift);

        // Assert
        fragment.Text.Should().Be(expected);
    }

    [Fact]
    public void UnsignedShiftPromotesItsLiteralAndConvertsTheCount() {
        // Arrange
        BinaryOperationNode shift = new(DataType.UINT32, new ConstantNode(DataType.UINT32, 1),
            BinaryOperation.LEFT_SHIFT, new RegisterNode(DataType.UINT32, (int)RegisterIndex.CxIndex));

        // Act
        CSharpFragment fragment = _printer.Print(shift);

        // Assert
        fragment.Text.Should().Be("1u << (int)ECX");
    }

    [Fact]
    public void UnsignedRightShiftTruncatesAPromotedLeftOperand() {
        // Arrange
        BinaryOperationNode difference = new(DataType.UINT32,
            new RegisterNode(DataType.UINT16, (int)RegisterIndex.AxIndex), BinaryOperation.MINUS,
            new RegisterNode(DataType.UINT16, (int)RegisterIndex.BxIndex));
        BinaryOperationNode shift = new(DataType.UINT32, difference, BinaryOperation.RIGHT_SHIFT,
            new ConstantNode(DataType.UINT32, 16));

        // Act
        CSharpFragment fragment = _printer.Print(shift);

        // Assert
        fragment.Text.Should().Be("(uint)(AX - BX) >> 16");
    }

    [Fact]
    public void ProtectedModeHelperArgumentsAreConvertedToTheirParameterTypes() {
        // Arrange
        InstructionExecutionHelperCallNode call = new(InstructionExecutionHelperOperation.LoadSegmentRegister,
            new ConstantNode(DataType.UINT32, 2),
            new BinaryOperationNode(DataType.UINT16,
                new RegisterNode(DataType.UINT16, (int)RegisterIndex.AxIndex), BinaryOperation.PLUS,
                new ConstantNode(DataType.UINT16, 1)));

        // Act
        CSharpFragment fragment = _printer.Print(call);

        // Assert
        fragment.Text.Should().Be("LoadSegmentRegister(2, (ushort)(AX + 1))");
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

    public static IEnumerable<object[]> CrossSignedCasts() {
        yield return [DataType.INT64, unchecked((ulong)-1L), DataType.UINT64, "ulong", ulong.MaxValue];
        yield return [DataType.UINT64, ulong.MaxValue, DataType.INT64, "long", -1L];
        yield return [DataType.UINT64, ulong.MaxValue, DataType.INT32, "int", -1];
        yield return [DataType.INT64, (ulong)long.MaxValue, DataType.UINT64, "ulong", (ulong)long.MaxValue];
        yield return [DataType.UINT32, (ulong)uint.MaxValue, DataType.INT64, "long", (long)uint.MaxValue];
    }

    /// <summary>A constant conversion whose literal does not fit the target's range must print the
    /// already converted constant: <c>(ulong)-1L</c> and <c>(long)0xFFFFFFFFFFFFFFFFUL</c> are
    /// CS0221 errors, so the emitted text is compiled, not only compared.</summary>
    [Theory]
    [MemberData(nameof(CrossSignedCasts))]
    public void ConstantCastAcrossSignednessCompilesToTheConvertedValue(DataType sourceType,
        ulong sourceValue, DataType targetType, string csharpType, object expected) {
        string emitted = _printer.Print(new TypeConversionNode(targetType,
            new ConstantNode(sourceType, sourceValue))).Text;

        GeneratedCompilation compilation = new GeneratedOverrideCompiler().Compile(
            [$"public static class Casts {{ public static {csharpType} Value() => {emitted}; }}"]);

        // Act
        using MemoryStream peStream = compilation.PeStream;
        compilation.EmitResult.Success.Should().BeTrue(
            $"cast '{emitted}' to {csharpType} must compile: "
            + string.Join("; ", compilation.EmitResult.Diagnostics));

        // Assert: the converted literal evaluates to the expected value.
        CollectibleAssemblyLoadContext context = new();
        try {
            peStream.Position = 0;
            Type type = context.LoadFromStream(peStream).GetType("Casts")
                ?? throw new InvalidOperationException("Compiled Casts type missing.");
            MethodInfo method = type.GetMethod("Value")
                ?? throw new InvalidOperationException("Compiled Value method missing.");
            method.Invoke(null, null).Should().Be(expected);
        } finally {
            context.Unload();
        }
    }

    /// <summary>A range pattern must keep the source's byte truncation: <c>(byte)(x + 1)</c>
    /// wraps, so <c>x + 1 is >= 1 and &lt;= 5</c> compared at int width would be wrong. The
    /// printed condition is compiled and evaluated for every byte input against the explicit
    /// conditional.</summary>
    [Fact]
    public void RangePatternPreservesSourceTruncation() {
        RegisterNode al = new(DataType.UINT8, (int)RegisterIndex.AxIndex);
        ValueNode sum = new BinaryOperationNode(DataType.UINT8, al, BinaryOperation.PLUS,
            new ConstantNode(DataType.UINT8, 1));
        BinaryOperationNode range = new(DataType.BOOL,
            new BinaryOperationNode(DataType.BOOL, sum,
                BinaryOperation.GREATER_THAN_OR_EQUAL, new ConstantNode(DataType.UINT8, 1)),
            BinaryOperation.LOGICAL_AND,
            new BinaryOperationNode(DataType.BOOL, sum,
                BinaryOperation.LESS_THAN_OR_EQUAL, new ConstantNode(DataType.UINT8, 5)));

        string emitted = _printer.Print(range).Text;
        GeneratedCompilation compilation = new GeneratedOverrideCompiler().Compile(
            [$"public static class Ranges {{ public static bool Holds(byte AL) => {emitted}; }}"]);
        compilation.EmitResult.Success.Should().BeTrue(
            $"range '{emitted}' must compile: "
            + string.Join("; ", compilation.EmitResult.Diagnostics));

        using MemoryStream peStream = compilation.PeStream;
        CollectibleAssemblyLoadContext context = new();
        try {
            peStream.Position = 0;
            MethodInfo method = context.LoadFromStream(peStream).GetType("Ranges")
                ?.GetMethod("Holds")
                ?? throw new InvalidOperationException("Compiled Ranges.Holds missing.");
            for (int x = 0; x < 256; x++) {
                bool expected = (byte)(x + 1) >= 1 && (byte)(x + 1) <= 5;
                method.Invoke(null, [(byte)x]).Should().Be(expected,
                    $"range '{emitted}' for x={x}");
            }
        } finally {
            context.Unload();
        }
    }

    [Fact]
    public void WideNonconstantPackDropsTheOuterUintCast() {
        // Arrange: (uint)((uint)AX | (uint)DX << 16): the OR evaluates at uint already.
        RegisterNode ax = new(DataType.UINT16, (int)RegisterIndex.AxIndex);
        RegisterNode dx = new(DataType.UINT16, (int)RegisterIndex.DxIndex);
        ValueNode low = new TypeConversionNode(DataType.UINT32, ax);
        ValueNode high = new BinaryOperationNode(DataType.UINT32,
            new TypeConversionNode(DataType.UINT32, dx), BinaryOperation.LEFT_SHIFT,
            new ConstantNode(DataType.INT32, 16));
        TypeConversionNode pack = new(DataType.UINT32,
            new BinaryOperationNode(DataType.UINT32, low, BinaryOperation.BITWISE_OR, high));

        CSharpFragment fragment = _printer.Print(pack);

        fragment.Text.Should().Be("(uint)AX | (uint)DX << 16");
    }

    [Fact]
    public void WidePackOrConstantDropsTheOuterUintCast() {
        // Arrange: (uint)((uint)DX << 16 | 0x0200): the literal promotes to uint, so
        // the OR still evaluates at uint and the outer cast is redundant.
        RegisterNode dx = new(DataType.UINT16, (int)RegisterIndex.DxIndex);
        ValueNode high = new BinaryOperationNode(DataType.UINT32,
            new TypeConversionNode(DataType.UINT32, dx), BinaryOperation.LEFT_SHIFT,
            new ConstantNode(DataType.INT32, 16));
        TypeConversionNode pack = new(DataType.UINT32,
            new BinaryOperationNode(DataType.UINT32, high, BinaryOperation.BITWISE_OR,
                new ConstantNode(DataType.UINT32, 0x0200)));

        CSharpFragment fragment = _printer.Print(pack);

        fragment.Text.Should().Be("(uint)DX << 16 | 0x00000200");
    }

    [Fact]
    public void ConstantOrWidePackDropsTheOuterUintCast() {
        // Arrange: the commuted form keeps the same proof.
        RegisterNode dx = new(DataType.UINT16, (int)RegisterIndex.DxIndex);
        ValueNode high = new BinaryOperationNode(DataType.UINT32,
            new TypeConversionNode(DataType.UINT32, dx), BinaryOperation.LEFT_SHIFT,
            new ConstantNode(DataType.INT32, 16));
        TypeConversionNode pack = new(DataType.UINT32,
            new BinaryOperationNode(DataType.UINT32,
                new ConstantNode(DataType.UINT32, 0x0200), BinaryOperation.BITWISE_OR,
                high));

        CSharpFragment fragment = _printer.Print(pack);

        fragment.Text.Should().Be("0x00000200 | (uint)DX << 16");
    }

    [Fact]
    public void NarrowArithmeticKeepsTheRequiredCast() {
        // Arrange: a word OR still promotes to int: the (ushort) wrap stays.
        RegisterNode ax = new(DataType.UINT16, (int)RegisterIndex.AxIndex);
        RegisterNode bx = new(DataType.UINT16, (int)RegisterIndex.BxIndex);
        TypeConversionNode narrow = new(DataType.UINT16,
            new BinaryOperationNode(DataType.UINT16, ax, BinaryOperation.BITWISE_OR, bx));

        CSharpFragment fragment = _printer.Print(narrow);

        fragment.Text.Should().Be("(ushort)(AX | BX)");
    }

    [Fact]
    public void AllConstantWidePackKeepsItsLiteralCast() {
        // Arrange: constant halves print literals whose CLR type need not match the node type,
        // so no result type is inferred and the cast stays.
        TypeConversionNode pack = new(DataType.UINT32,
            new BinaryOperationNode(DataType.UINT32,
                new ConstantNode(DataType.UINT32, 1), BinaryOperation.BITWISE_OR,
                new BinaryOperationNode(DataType.UINT32,
                    new ConstantNode(DataType.UINT32, 2), BinaryOperation.LEFT_SHIFT,
                    new ConstantNode(DataType.INT32, 16))));

        CSharpFragment fragment = _printer.Print(pack);

        fragment.Text.Should().Be("(uint)(1 | 2 << 16)");
    }

    private CSharpFragment StackWord(ValueNode offset) {
        SegmentedPointerNode pointer = new(DataType.UINT16, new SegmentRegisterNode(2), null, offset);
        return _printer.Print(pointer);
    }
}
