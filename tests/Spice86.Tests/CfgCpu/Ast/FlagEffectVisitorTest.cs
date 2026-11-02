namespace Spice86.Tests.CfgCpu.Ast;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Shared.Emulator.Memory;

using Xunit;

public class FlagEffectVisitorTest {
    private const StatusFlags ShiftFlags = StatusFlags.Carry | StatusFlags.Parity | StatusFlags.Zero | StatusFlags.Sign | StatusFlags.Overflow;
    private static readonly RegisterNode Ax = new(DataType.UINT16, 0);
    private static readonly RegisterNode Cx = new(DataType.UINT16, 1);
    private static readonly RegisterNode Bx = new(DataType.UINT16, 3);
    private static readonly RegisterNode Cl = new(DataType.UINT8, 1);

    [Fact]
    public void CompareWritesEveryStatusFlag() {
        // Act
        FlagEffect effect = FlagEffectVisitor.Of(Alu16("Sub", Ax, Bx));

        // Assert
        effect.Should().Be(FlagEffect.Writing(StatusFlags.All));
    }

    [Fact]
    public void AddWithCarryReadsCarry() {
        // Act
        FlagEffect effect = FlagEffectVisitor.Of(Assign(Ax, Alu16("Adc", Ax, Bx)));

        // Assert
        effect.Should().Be(new FlagEffect(StatusFlags.Carry, StatusFlags.All, StatusFlags.All));
    }

    [Fact]
    public void IncrementKeepsCarry() {
        // Act
        FlagEffect effect = FlagEffectVisitor.Of(Assign(Ax, Alu16("Inc", Ax)));

        // Assert
        effect.Should().Be(FlagEffect.Writing(StatusFlags.All & ~StatusFlags.Carry));
    }

    [Fact]
    public void ShiftCountDecidesBetweenMustAndMayWrite() {
        // Act
        FlagEffect byOne = FlagEffectVisitor.Of(Assign(Ax, Alu16("Shl", Ax, new ConstantNode(DataType.UINT8, 1))));
        FlagEffect byMaskedZero = FlagEffectVisitor.Of(Assign(Ax, Alu16("Shl", Ax, new ConstantNode(DataType.UINT8, 0x20))));
        FlagEffect byCl = FlagEffectVisitor.Of(Assign(Ax, Alu16("Shl", Ax, Cl)));

        // Assert
        byOne.Should().Be(FlagEffect.Writing(ShiftFlags));
        byMaskedZero.Should().Be(FlagEffect.None);
        byCl.Should().Be(new FlagEffect(StatusFlags.None, StatusFlags.None, ShiftFlags));
    }

    [Fact]
    public void ConditionalJumpReadsItsCondition() {
        // Arrange
        BinaryOperationNode belowOrEqual = new(DataType.BOOL, Flag(Flags.Carry), BinaryOperation.LOGICAL_OR, Flag(Flags.Zero));

        // Act
        FlagEffect effect = FlagEffectVisitor.Of(new IfElseNode(belowOrEqual, new BlockNode(), new BlockNode()));

        // Assert
        effect.Should().Be(FlagEffect.Reading(StatusFlags.Carry | StatusFlags.Zero));
    }

    [Fact]
    public void FlagWrittenInBothArmsIsAMustWrite() {
        // Arrange
        BinaryOperationNode sourceNotZero = new(DataType.BOOL, Ax, BinaryOperation.NOT_EQUAL, new ConstantNode(DataType.UINT16, 0));
        IfElseNode bitScan = new(sourceNotZero,
            new BlockNode(Assign(Bx, new BitScanCallNode(DataType.UINT16, BitScanDirection.Forward, Ax)), Assign(Flag(Flags.Zero), False())),
            new BlockNode(Assign(Flag(Flags.Zero), True())));
        IfElseNode oneArm = new(sourceNotZero, new BlockNode(Assign(Flag(Flags.Carry), True())), new BlockNode());

        // Act
        FlagEffect bothArms = FlagEffectVisitor.Of(bitScan);
        FlagEffect singleArm = FlagEffectVisitor.Of(oneArm);

        // Assert
        bothArms.Should().Be(FlagEffect.Writing(StatusFlags.Zero));
        singleArm.Should().Be(new FlagEffect(StatusFlags.None, StatusFlags.None, StatusFlags.Carry));
    }

    [Fact]
    public void LoopBodyMayRunZeroTimes() {
        // Arrange
        BinaryOperationNode counterNotZero = new(DataType.BOOL, Cx, BinaryOperation.NOT_EQUAL, new ConstantNode(DataType.UINT16, 0));
        WhileNode loop = new(counterNotZero, new BlockNode(Alu16("Sub", Ax, Bx)));

        // Act
        FlagEffect effect = FlagEffectVisitor.Of(loop);

        // Assert
        effect.Should().Be(new FlagEffect(StatusFlags.None, StatusFlags.None, StatusFlags.All));
    }

    [Fact]
    public void LoopExitTestReadsFlagsTheBodyDidNotWrite() {
        // Arrange
        BinaryOperationNode counterNotZero = new(DataType.BOOL, Cx, BinaryOperation.NOT_EQUAL, new ConstantNode(DataType.UINT16, 0));
        BinaryOperationNode zeroFlagClear = new(DataType.BOOL, Flag(Flags.Zero), BinaryOperation.EQUAL, False());
        WhileWithExitNode decrementOnly = new(counterNotZero,
            new BlockNode(Assign(Cx, new BinaryOperationNode(DataType.UINT16, Cx, BinaryOperation.MINUS, new ConstantNode(DataType.UINT16, 1)))),
            zeroFlagClear);
        WhileWithExitNode compareLoop = new(counterNotZero, new BlockNode(Alu16("Sub", Ax, Bx)), zeroFlagClear);

        // Act
        FlagEffect decrementEffect = FlagEffectVisitor.Of(decrementOnly);
        FlagEffect compareEffect = FlagEffectVisitor.Of(compareLoop);

        // Assert
        decrementEffect.Should().Be(FlagEffect.Reading(StatusFlags.Zero));
        compareEffect.Should().Be(new FlagEffect(StatusFlags.None, StatusFlags.None, StatusFlags.All));
    }

    [Fact]
    public void ReadAfterWriteIsNotARead() {
        // Act
        FlagEffect effect = FlagEffectVisitor.Of(new BlockNode(Assign(Flag(Flags.Carry), False()), Assign(Ax, Alu16("Adc", Ax, Bx))));

        // Assert
        effect.Should().Be(FlagEffect.Writing(StatusFlags.All));
    }

    [Fact]
    public void FlagRegisterIsReadAndWrittenAsAWhole() {
        // Arrange
        FlagRegisterNode flags16 = new(DataType.UINT16);
        FlagRegisterNode flags32 = new(DataType.UINT32);
        MethodCallNode pushf = new StackCallNode(StackOperation.Push, BitWidth.WORD_16, flags16);
        BinaryOperationNode popf = Assign(flags16, new StackCallValueNode(StackOperation.Pop, BitWidth.WORD_16));
        BinaryOperationNode keptUpperBits = new(DataType.UINT32, flags32, BinaryOperation.BITWISE_AND, new ConstantNode(DataType.UINT32, 0xFFFFFF00));
        BinaryOperationNode sahf = Assign(flags32, new BinaryOperationNode(DataType.UINT32, keptUpperBits, BinaryOperation.BITWISE_OR,
            new TypeConversionNode(DataType.UINT32, new RegisterNode(DataType.UINT8, 4))));

        // Assert
        FlagEffectVisitor.Of(pushf).Should().Be(FlagEffect.Reading(StatusFlags.All));
        FlagEffectVisitor.Of(popf).Should().Be(FlagEffect.Writing(StatusFlags.All));
        FlagEffectVisitor.Of(sahf).Should().Be(new FlagEffect(StatusFlags.All, StatusFlags.All, StatusFlags.All));
    }

    [Fact]
    public void NonStatusFlagsAreIgnored() {
        // Assert
        FlagEffectVisitor.Of(Assign(Flag(Flags.Direction), True())).Should().Be(FlagEffect.None);
        FlagEffectVisitor.Of(new IfElseNode(Flag(Flags.Interrupt), new BlockNode(), new BlockNode())).Should().Be(FlagEffect.None);
    }

    [Fact]
    public void KnownHelpersAreNeutralAndUnknownCallsAreConservative() {
        // Arrange
        MethodCallNode output = new PortWriteCallNode(BitWidth.BYTE_8, new ConstantNode(DataType.UINT16, 0x60), new RegisterNode(DataType.UINT8, 0));
        ConditionalValueNode direction = AstBuilder.DirectionStep(BitWidth.WORD_16);
        MethodCallNode unknown = new(null, "Foo");

        // Assert
        FlagEffectVisitor.Of(output).Should().Be(FlagEffect.None);
        FlagEffectVisitor.Of(direction).Should().Be(FlagEffect.None);
        FlagEffectVisitor.Of(unknown).Should().Be(FlagEffect.Unknown);
        FlagEffectVisitor.Of(new MethodCallValueNode(DataType.UINT16, "Alu16", "Frobnicate", Ax)).Should().Be(FlagEffect.Unknown);
    }

    private static AluCallValueNode Alu16(string operation, params ValueNode[] operands) =>
        new(DataType.UINT16, Enum.Parse<AluOperation>(operation), BitWidth.WORD_16, operands);

    private static BinaryOperationNode Assign(ValueNode target, ValueNode value) =>
        new(target.DataType, target, BinaryOperation.ASSIGN, value);

    private static CpuFlagNode Flag(ushort mask) => new(mask);

    private static ConstantNode True() => new(DataType.BOOL, 1);

    private static ConstantNode False() => new(DataType.BOOL, 0);
}
