using System.Collections.Immutable;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Logging;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using Xunit;

namespace Spice86.Tests.CfgCpu;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
using Spice86.Core.Emulator.CPU.Registers;

public class InstructionsFeederTest {
    private static readonly SegmentedAddress ZeroAddress = new(0, 0);
    private static readonly SegmentedAddress TwoAddress = new(0, 2);
    private static readonly SegmentedAddress SixteenAddressViaOffset = new(0, 16);
    private static readonly SegmentedAddress SixteenAddressViaSegment = new(1, 0);
    private readonly Memory _memory = new(new Ram(64), new A20Gate());

    private InstructionsFeeder CreateInstructionsFeeder() {
        _memory.Memset8(0, 0, 64);
        ILoggerService loggerService = Substitute.For<LoggerService>(new LoggerPropertyBag());
        State state = new(new Flags(), new GeneralRegisters(), new SegmentRegisters());
        MachineBreakpoints machineBreakpoints = new MachineBreakpoints(_memory, state, loggerService);
        return new InstructionsFeeder(new CurrentInstructions(_memory, machineBreakpoints), new InstructionParser(_memory, state), new PreviousInstructions(_memory));
    }

    private void WriteJumpNear(SegmentedAddress address) {
        _memory.UInt8[address] = 0xEB;
        _memory.Int8[address + 1] = -2;
    }

    private void WriteMovAx(SegmentedAddress address) {
        _memory.UInt8[address] = 0xB8;
        _memory.UInt16[address + 1] = 0xFFFF;
    }

    private JmpNearImm8 CreateReplacementInstruction() {
        var opcodeField = new InstructionField<ushort>(0, 1, 0, 0xEB, ImmutableList.Create<byte?>(0xEB), true);
        // Replacement has a null discriminator byte for offset field -> will not be taken into account when comparing with ram
        var offsetField = new InstructionField<sbyte>(1, 1, 1, -2, ImmutableList.Create((byte?)null), false);
        JmpNearImm8 res = new JmpNearImm8(ZeroAddress, opcodeField, new List<InstructionPrefix>(), offsetField);
        res.PostInit();
        return res;
    }

    [Fact]
    public void ReadInstructionViaParser() {
        // Arrange
        InstructionsFeeder instructionsFeeder = CreateInstructionsFeeder();
        WriteJumpNear(ZeroAddress);

        // Act
        CfgInstruction instruction = instructionsFeeder.GetInstructionFromMemory(ZeroAddress);

        // Assert
        Assert.Equal(typeof(JmpNearImm8), instruction.GetType());
    }

    [Fact]
    public void ReadInstructionTwiceIsSameInstruction() {
        // Arrange
        InstructionsFeeder instructionsFeeder = CreateInstructionsFeeder();
        WriteJumpNear(ZeroAddress);

        // Act
        CfgInstruction instruction1 = instructionsFeeder.GetInstructionFromMemory(ZeroAddress);
        CfgInstruction instruction2 = instructionsFeeder.GetInstructionFromMemory(ZeroAddress);


        // Assert
        Assert.Equal(instruction1, instruction2);
    }

    [Fact]
    public void ReadSelfModifyingCode() {
        // Arrange
        InstructionsFeeder instructionsFeeder = CreateInstructionsFeeder();
        WriteJumpNear(ZeroAddress);

        // Act
        // Read the jump
        instructionsFeeder.GetInstructionFromMemory(ZeroAddress);
        // This should cause the breakpoints to trigger and to clear the first instruction from the first current cache ensuring address 0 will be parsed
        WriteMovAx(ZeroAddress);
        CfgInstruction instruction2 = instructionsFeeder.GetInstructionFromMemory(ZeroAddress);


        // Assert
        Assert.Equal(typeof(MovRegImm16), instruction2.GetType());
    }

    [Fact]
    public void ReadSelfModifyingCodeSameInstructionTwice() {
        // Arrange
        InstructionsFeeder instructionsFeeder = CreateInstructionsFeeder();
        WriteJumpNear(ZeroAddress);

        // Act
        // Read the jump
        CfgInstruction instruction1 = instructionsFeeder.GetInstructionFromMemory(ZeroAddress);
        WriteMovAx(ZeroAddress);
        // Read the mov
        instructionsFeeder.GetInstructionFromMemory(ZeroAddress);
        WriteJumpNear(ZeroAddress);
        // Read the jump again
        CfgInstruction instruction2 = instructionsFeeder.GetInstructionFromMemory(ZeroAddress);

        // Assert
        Assert.Equal(instruction1, instruction2);
    }

    [Fact]
    public void ReadSelfModifyingCodeSameInstructionTwiceThenDetectSwitchAgain() {
        // Arrange
        InstructionsFeeder instructionsFeeder = CreateInstructionsFeeder();
        WriteJumpNear(ZeroAddress);

        // Act
        // Read the jump
        instructionsFeeder.GetInstructionFromMemory(ZeroAddress);
        WriteMovAx(ZeroAddress);
        // Read the mov
        instructionsFeeder.GetInstructionFromMemory(ZeroAddress);
        WriteJumpNear(ZeroAddress);
        // Read the jump again
        instructionsFeeder.GetInstructionFromMemory(ZeroAddress);
        WriteMovAx(ZeroAddress);
        // Read the mov, normally last restore that is from "previous" cache should also have set breakpoints and so on for self modifying code detection
        CfgInstruction instruction2 = instructionsFeeder.GetInstructionFromMemory(ZeroAddress);

        // Assert
        Assert.Equal(typeof(MovRegImm16), instruction2.GetType());
    }

    [Fact]
    public void ReplaceInstruction() {
        // Arrange
        InstructionsFeeder instructionsFeeder = CreateInstructionsFeeder();
        WriteJumpNear(ZeroAddress);
        CfgInstruction old = instructionsFeeder.GetInstructionFromMemory(ZeroAddress);

        // Act
        CfgInstruction newInstruction = CreateReplacementInstruction();
        instructionsFeeder.ReplaceInstruction(old, newInstruction);
        CfgInstruction instruction = instructionsFeeder.GetInstructionFromMemory(ZeroAddress);

        // Assert
        Assert.Equal(newInstruction, instruction);
    }

    [Fact]
    public void ReplaceInstructionAndEnsureSelfModifyingCodeIsDetected() {
        // Arrange
        InstructionsFeeder instructionsFeeder = CreateInstructionsFeeder();
        WriteJumpNear(ZeroAddress);
        CfgInstruction old = instructionsFeeder.GetInstructionFromMemory(ZeroAddress);

        // Act
        CfgInstruction newInstruction = CreateReplacementInstruction();
        instructionsFeeder.ReplaceInstruction(old, newInstruction);
        WriteMovAx(ZeroAddress);
        CfgInstruction instruction = instructionsFeeder.GetInstructionFromMemory(ZeroAddress);

        // Assert
        Assert.Equal(typeof(MovRegImm16), instruction.GetType());
    }

    [Fact]
    public void ReplaceInstructionAndEnsureItStaysAfterSelfModifyingCode() {
        // Arrange
        InstructionsFeeder instructionsFeeder = CreateInstructionsFeeder();
        WriteJumpNear(ZeroAddress);
        CfgInstruction old = instructionsFeeder.GetInstructionFromMemory(ZeroAddress);

        // Act
        CfgInstruction newInstruction = CreateReplacementInstruction();
        instructionsFeeder.ReplaceInstruction(old, newInstruction);
        WriteMovAx(ZeroAddress);
        instructionsFeeder.GetInstructionFromMemory(ZeroAddress);
        WriteJumpNear(ZeroAddress);
        CfgInstruction instruction = instructionsFeeder.GetInstructionFromMemory(ZeroAddress);

        // Assert
        Assert.Equal(newInstruction, instruction);
    }

    [Fact]
    public void SameInstructionAsDifferentAddressIsDifferentNode() {
        // Arrange
        InstructionsFeeder instructionsFeeder = CreateInstructionsFeeder();
        WriteJumpNear(ZeroAddress);
        WriteJumpNear(TwoAddress);

        // Act
        CfgInstruction instructionAddress0 = instructionsFeeder.GetInstructionFromMemory(ZeroAddress);
        CfgInstruction instructionAddress2 = instructionsFeeder.GetInstructionFromMemory(TwoAddress);


        // Assert
        Assert.Equal(typeof(JmpNearImm8), instructionAddress2.GetType());
        Assert.NotEqual(instructionAddress0, instructionAddress2);
    }

    [Fact]
    public void SameInstructionSamePhysicalAddressDifferentSegmentedAddressIsSame() {
        // Arrange
        InstructionsFeeder instructionsFeeder = CreateInstructionsFeeder();
        WriteJumpNear(SixteenAddressViaOffset);

        // Act
        CfgInstruction instruction1 = instructionsFeeder.GetInstructionFromMemory(SixteenAddressViaOffset);
        CfgInstruction instruction2 = instructionsFeeder.GetInstructionFromMemory(SixteenAddressViaSegment);


        // Assert
        Assert.Equal(instruction1, instruction2);
    }
}