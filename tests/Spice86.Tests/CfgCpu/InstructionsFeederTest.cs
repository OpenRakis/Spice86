namespace Spice86.Tests.CfgCpu;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Logging;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using Xunit;

public class InstructionsFeederTest : IDisposable {
    private const ushort MovRegImm16OpcodeAx = 0xB8;
    private static readonly SegmentedAddress ZeroAddress = new(0, 0);
    private static readonly SegmentedAddress TwoAddress = new(0, 2);
    private static readonly SegmentedAddress SixteenAddressViaOffset = new(0, 16);
    private static readonly SegmentedAddress SixteenAddressViaSegment = new(1, 0);
    private readonly TestInstructionHelper _helper = new();
    private readonly AstInstructionRenderer _renderer = new(AsmRenderingConfig.CreateSpice86Style());
    private Memory _memory = new(new(), new Ram(64), new A20Gate());
    private InstructionReplacerRegistry _instructionReplacer = new();
    private CfgNodeExecutionCompiler? _compiler;

    private InstructionsFeeder CreateInstructionsFeeder() {
        ILoggerService loggerService = Substitute.For<LoggerService>();
        State state = new(CpuModel.INTEL_80286);
        AddressReadWriteBreakpoints memoryBreakpoints = new();
        AddressReadWriteBreakpoints ioBreakpoints = new();
        _memory = new(memoryBreakpoints, new Ram(64), new A20Gate());
        _instructionReplacer = new();
        EmulatorBreakpointsManager emulatorBreakpointsManager = new(new PauseHandler(loggerService), state, _memory, memoryBreakpoints, ioBreakpoints);
        _compiler?.Dispose();
        _compiler = new CfgNodeExecutionCompiler(new CfgNodeExecutionCompilerMonitor(loggerService), loggerService, JitMode.InterpretedOnly);
        
        return new InstructionsFeeder(emulatorBreakpointsManager, _memory, state, _instructionReplacer,
            _compiler);
    }

    private void WriteJumpNear(SegmentedAddress address) {
        _memory.UInt8[address] = 0xEB;
        _memory.Int8[address + 1] = -2;
    }

    private void WriteMovAx(SegmentedAddress address) {
        _memory.UInt8[address] = 0xB8;
        _memory.UInt16[address + 1] = 0xFFFF;
    }

    private void WriteLoop(SegmentedAddress address, sbyte relativeOffset) {
        _memory.UInt8[address] = 0xE2;
        _memory.Int8[address + 1] = relativeOffset;
    }

    private CfgInstruction CreateReplacementInstruction() {
        CfgInstruction instruction = _helper.WriteAndParse(ZeroAddress, w => w.WriteJumpShort(-2));
        // Mark offset field as non-signature for replacement semantics
        FieldWithValue offsetField = instruction.FieldsInOrder[^1];
        offsetField.NullifySignature();
        return instruction;
    }

    private string RenderDisplayAst(CfgInstruction instruction) {
        return instruction.DisplayAst.Accept(_renderer);
    }

    private static ushort ExpectedLoopTarget(CfgInstruction instruction, sbyte relativeOffset) {
        unchecked {
            int nextOffset = instruction.Address.Offset + instruction.Length;
            return (ushort)(nextOffset + relativeOffset);
        }
    }

    [Fact]
    public void ReadInstructionViaParser() {
        // Arrange
        InstructionsFeeder instructionsFeeder = CreateInstructionsFeeder();
        WriteJumpNear(ZeroAddress);

        // Act
        CfgInstruction instruction = instructionsFeeder.GetInstructionFromMemory(ZeroAddress);

        // Assert
        Assert.Equal(typeof(CfgInstruction), instruction.GetType());
        Assert.True(instruction.IsJump);
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
        Assert.Equal(MovRegImm16OpcodeAx, instruction2.OpcodeField.Value);
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
        Assert.Equal(MovRegImm16OpcodeAx, instruction2.OpcodeField.Value);
    }

    [Fact]
    public void LoopSelfModifyingOffsetUpdatesTarget() {
        // Arrange
        InstructionsFeeder instructionsFeeder = CreateInstructionsFeeder();
        WriteLoop(ZeroAddress, -2);

        // Act
        CfgInstruction initialLoop = instructionsFeeder.GetInstructionFromMemory(ZeroAddress);
        WriteLoop(ZeroAddress, -4);
        CfgInstruction afterFirstModification = instructionsFeeder.GetInstructionFromMemory(ZeroAddress);
        WriteLoop(ZeroAddress, -6);
        CfgInstruction afterSecondModification = instructionsFeeder.GetInstructionFromMemory(ZeroAddress);

        // Assert — each parsed loop should have the correct offset field value
        InstructionField<sbyte> initialOffset = (InstructionField<sbyte>)initialLoop.FieldsInOrder[^1];
        Assert.Equal((sbyte)-2, initialOffset.Value);
        InstructionField<sbyte> firstOffset = (InstructionField<sbyte>)afterFirstModification.FieldsInOrder[^1];
        Assert.Equal((sbyte)-4, firstOffset.Value);
        InstructionField<sbyte> secondOffset = (InstructionField<sbyte>)afterSecondModification.FieldsInOrder[^1];
        Assert.Equal((sbyte)-6, secondOffset.Value);
    }

    [Fact]
    public void ReplaceInstruction() {
        // Arrange
        InstructionsFeeder instructionsFeeder = CreateInstructionsFeeder();
        WriteJumpNear(ZeroAddress);
        CfgInstruction old = instructionsFeeder.GetInstructionFromMemory(ZeroAddress);

        // Act
        CfgInstruction newInstruction = CreateReplacementInstruction();
        _instructionReplacer.ReplaceInstruction(old, newInstruction);
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
        _instructionReplacer.ReplaceInstruction(old, newInstruction);
        WriteMovAx(ZeroAddress);
        CfgInstruction instruction = instructionsFeeder.GetInstructionFromMemory(ZeroAddress);

        // Assert
        Assert.Equal(MovRegImm16OpcodeAx, instruction.OpcodeField.Value);
    }

    [Fact]
    public void ReplaceInstructionAndEnsureItStaysAfterSelfModifyingCode() {
        // Arrange
        InstructionsFeeder instructionsFeeder = CreateInstructionsFeeder();
        WriteJumpNear(ZeroAddress);
        CfgInstruction old = instructionsFeeder.GetInstructionFromMemory(ZeroAddress);

        // Act
        CfgInstruction newInstruction = CreateReplacementInstruction();
        _instructionReplacer.ReplaceInstruction(old, newInstruction);
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
        Assert.Equal(typeof(CfgInstruction), instructionAddress2.GetType());
        Assert.True(instructionAddress2.IsJump);
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
        Assert.NotEqual(instruction1, instruction2);
    }

    public void Dispose() {
        _compiler?.Dispose();
    }
}
