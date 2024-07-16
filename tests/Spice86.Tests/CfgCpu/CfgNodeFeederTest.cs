using JetBrains.Annotations;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Logging;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using Xunit;

namespace Spice86.Tests.CfgCpu;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.IOPorts;

public class CfgNodeFeederTest {
    private const int AxIndex = 0;
    private const int BxIndex = 3;
    private const int CxIndex = 1;
    private const ushort DefaultValue = 0xFFFF;
    private const ushort NewValue = 0x1234;
    private const int MovRegImm16Length = 3;
    private static readonly SegmentedAddress ZeroAddress = new(0, 0);
    private static readonly SegmentedAddress EndOfMov0Address = new(0, MovRegImm16Length);

    private Memory _memory = new(new Ram(64), new A20Gate());
    private State _state = new(new Flags(), new GeneralRegisters(), new SegmentRegisters());

    private CfgNodeFeeder CreateCfgNodeFeeder() {
        ILoggerService loggerService = Substitute.For<LoggerService>(new LoggerPropertyBag());
        _memory = new(new Ram(64), new A20Gate());
        _state = new State(new Flags(), new GeneralRegisters(), new SegmentRegisters());
        IOPortDispatcher ioPortDispatcher = new IOPortDispatcher(_state, loggerService, failOnUnhandledPort: true);
        CallbackHandler callbackHandler = new(_state, loggerService);
        MachineBreakpoints machineBreakpoints = new MachineBreakpoints(_memory, _state, loggerService);
        InstructionExecutionHelper instructionExecutionHelper = new(_state, _memory, ioPortDispatcher, callbackHandler, loggerService);
        ExecutionContextManager executionContextManager = new(machineBreakpoints, new ExecutionContext());
        NodeLinker nodeLinker = new();
        InstructionsFeeder instructionsFeeder = new(machineBreakpoints, _memory, _state);
        return new(instructionsFeeder, new(new List<IInstructionReplacer<CfgInstruction>>()
            { nodeLinker, instructionsFeeder }), nodeLinker, _state);
    }

    private void WriteMovReg16(SegmentedAddress address, byte opcode, ushort value) {
        _memory.UInt8[address] = opcode;
        _memory.UInt16[address + 1] = value;
    }

    private void WriteMovAx(SegmentedAddress address, ushort value) {
        WriteMovReg16(address, 0xB8, value);
    }

    private void WriteMovBx(SegmentedAddress address, ushort value) {
        WriteMovReg16(address, 0xBB, value);
    }

    private void WriteMovCx(SegmentedAddress address, ushort value) {
        WriteMovReg16(address, 0xB9, value);
    }

    private ICfgNode SimulateExecution(CfgNodeFeeder cfgNodeFeeder, ExecutionContext executionContext) {
        ICfgNode node = cfgNodeFeeder.GetLinkedCfgNodeToExecute(executionContext);
        executionContext.LastExecuted = node;
        if (node is CfgInstruction cfgInstruction) {
            _state.IP = cfgInstruction.Length;
        }

        return node;
    }

    private void WriteTwoMovAx() {
        WriteMovAx(ZeroAddress, DefaultValue);
        WriteMovAx(EndOfMov0Address, DefaultValue);
    }

    [Fact]
    public void ReadInstructionViaParser() {
        // Arrange
        CfgNodeFeeder cfgNodeFeeder = CreateCfgNodeFeeder();
        ExecutionContext executionContext = new ExecutionContext();
        WriteMovAx(ZeroAddress, DefaultValue);

        // Act
        ICfgNode movAx = cfgNodeFeeder.GetLinkedCfgNodeToExecute(executionContext);

        // Assert
        MovRegImm16 movAxRegImm16 = AssertIsMovAx(movAx);
        AssertUsesValue(movAxRegImm16, DefaultValue);
    }

    [Fact]
    public void LinkTwoInstructions() {
        // Arrange
        CfgNodeFeeder cfgNodeFeeder = CreateCfgNodeFeeder();
        ExecutionContext executionContext = new ExecutionContext();
        WriteMovAx(ZeroAddress, DefaultValue);
        WriteMovBx(EndOfMov0Address, DefaultValue);
        ICfgNode movAx = SimulateExecution(cfgNodeFeeder, executionContext);

        // Act
        ICfgNode movBx = cfgNodeFeeder.GetLinkedCfgNodeToExecute(executionContext);

        // Assert
        MovRegImm16 movAxRegImm16 = AssertIsMovAx(movAx);
        MovRegImm16 movBxRegImm16 = AssertIsMovBx(movBx);
        AssertUsesValue(movBxRegImm16, DefaultValue);
        AssertSuccessorAtAddress(movAxRegImm16, EndOfMov0Address, movBx);
    }

    [Fact]
    public void MovAxChangedToMovBx() {
        // Arrange
        CfgNodeFeeder cfgNodeFeeder = CreateCfgNodeFeeder();
        ExecutionContext executionContext = new ExecutionContext();
        WriteTwoMovAx();
        ICfgNode movAx0 = SimulateExecution(cfgNodeFeeder, executionContext);
        // Parse second Mov AX and insert it in graph
        ICfgNode movAx1 = cfgNodeFeeder.GetLinkedCfgNodeToExecute(executionContext);


        // Act
        // Some time later second Mov AX changes to Mov BX and we are about to execute it, graph says next is Mov AX but this is no the case.
        WriteMovBx(EndOfMov0Address, DefaultValue);
        executionContext.NodeToExecuteNextAccordingToGraph = movAx1;
        ICfgNode discriminated = cfgNodeFeeder.GetLinkedCfgNodeToExecute(executionContext);

        // Assert
        // Mov AX0 is not anymore linked to mov ax1, there is a discriminator node between them.
        AssertDoesNotLinkTo(movAx0, movAx1);
        // Check the discriminator node is there
        DiscriminatedNode discriminatedNode = AssertIsDiscriminatedNode(discriminated);
        AssertLinksTo(movAx0, discriminatedNode);
        // Check discriminator node also contains Mov BX
        ICfgNode movBx = discriminated.Successors.First(node => !ReferenceEquals(node, movAx1));
        MovRegImm16 movBxRegImm16 = AssertIsMovBx(movBx);
        MovRegImm16 movAx1RegImm16 = AssertIsMovAx(movAx1);
        AssertUsesValue(movBxRegImm16, DefaultValue);
        AssertSuccessorAtDiscriminator(discriminatedNode, movBxRegImm16);
        AssertSuccessorAtDiscriminator(discriminatedNode, movAx1RegImm16);
    }

    [Fact]
    public void MovAxChangedValue() {
        // Arrange
        CfgNodeFeeder cfgNodeFeeder = CreateCfgNodeFeeder();
        ExecutionContext executionContext = new ExecutionContext();
        WriteTwoMovAx();
        SimulateExecution(cfgNodeFeeder, executionContext);
        // Just parse next and insert it in graph
        ICfgNode movAx1 = cfgNodeFeeder.GetLinkedCfgNodeToExecute(executionContext);

        // Act
        // We are still after movAx0 but it changed to MOV AX 1234.
        WriteMovAx(EndOfMov0Address, NewValue);
        executionContext.NodeToExecuteNextAccordingToGraph = movAx1;
        ICfgNode movAx1WithNullDiscriminator = cfgNodeFeeder.GetLinkedCfgNodeToExecute(executionContext);

        // Assert
        AssertMovAxIsSameButValueFieldRefersMemory(movAx1, movAx1WithNullDiscriminator);
    }

    [Fact]
    public void MovAxChangedToMovBxThenMovCx() {
        // Arrange
        CfgNodeFeeder cfgNodeFeeder = CreateCfgNodeFeeder();
        ExecutionContext executionContext = new ExecutionContext();
        WriteTwoMovAx();
        SimulateExecution(cfgNodeFeeder, executionContext);
        ICfgNode movAx1 = cfgNodeFeeder.GetLinkedCfgNodeToExecute(executionContext);
        executionContext.NodeToExecuteNextAccordingToGraph = movAx1;
        WriteMovBx(EndOfMov0Address, DefaultValue);
        ICfgNode discriminated = cfgNodeFeeder.GetLinkedCfgNodeToExecute(executionContext);
        WriteMovCx(EndOfMov0Address, DefaultValue);
        // CPU executed discriminator but Mov CX was in memory => no successor of the discriminator matched
        executionContext.LastExecuted = discriminated;
        executionContext.NodeToExecuteNextAccordingToGraph = null;

        // Act
        // We at discriminator but instruction got changed to something that is not yet in the discriminator list of values.
        ICfgNode movCx = cfgNodeFeeder.GetLinkedCfgNodeToExecute(executionContext);

        // Assert
        DiscriminatedNode discriminatedNode = AssertIsDiscriminatedNode(discriminated);
        MovRegImm16 movCxRegImm16 = AssertIsMovCx(movCx);
        AssertSuccessorAtDiscriminator(discriminatedNode, movCxRegImm16);
    }

    [Fact]
    public void MovAxChangedToMovBxThenMovAxWithDifferentValue() {
        // Arrange
        CfgNodeFeeder cfgNodeFeeder = CreateCfgNodeFeeder();
        ExecutionContext executionContext = new ExecutionContext();
        WriteTwoMovAx();
        SimulateExecution(cfgNodeFeeder, executionContext);
        ICfgNode movAx1 = cfgNodeFeeder.GetLinkedCfgNodeToExecute(executionContext);
        // Code goes back to mov AX1 but it has been changed to MOV BX, discriminated node should have been created
        executionContext.NodeToExecuteNextAccordingToGraph = movAx1;
        WriteMovBx(EndOfMov0Address, DefaultValue);
        ICfgNode discriminated = cfgNodeFeeder.GetLinkedCfgNodeToExecute(executionContext);
        // Then write MOV AX back but with a different value
        WriteMovAx(EndOfMov0Address, NewValue);
        // CPU executed discriminator node, and determined Mov AX was next according to graph since it was in memory => should match with initial mov AX
        executionContext.LastExecuted = discriminated;
        executionContext.NodeToExecuteNextAccordingToGraph = movAx1;

        // Act
        ICfgNode movAx1WithNullDiscriminator = cfgNodeFeeder.GetLinkedCfgNodeToExecute(executionContext);

        // Assert
        AssertMovAxIsSameButValueFieldRefersMemory(movAx1, movAx1WithNullDiscriminator);
    }

    [AssertionMethod]
    private static void AssertSuccessorAtDiscriminator(DiscriminatedNode predecessor, CfgInstruction expectedSuccessor) {
        AssertLinksTo(predecessor, expectedSuccessor);
        Assert.Equal(expectedSuccessor, predecessor.SuccessorsPerDiscriminator[expectedSuccessor.Discriminator]);
    }

    private void AssertSuccessorAtAddress(CfgInstruction predecessor, SegmentedAddress address,
        ICfgNode expectedSuccessor) {
        AssertLinksTo(predecessor, expectedSuccessor);
        Assert.Equal(expectedSuccessor, predecessor.SuccessorsPerAddress[address]);
    }

    [AssertionMethod]
    private static void AssertLinksTo(ICfgNode previous, ICfgNode next) {
        Assert.Contains(next, previous.Successors);
        Assert.Contains(previous, next.Predecessors);
    }

    [AssertionMethod]
    private static void AssertDoesNotLinkTo(ICfgNode previous, ICfgNode next) {
        Assert.DoesNotContain(next, previous.Successors);
        Assert.DoesNotContain(previous, next.Predecessors);
    }

    [AssertionMethod]
    private static DiscriminatedNode AssertIsDiscriminatedNode(ICfgNode discriminated) {
        Assert.Equal(typeof(DiscriminatedNode), discriminated.GetType());
        return (DiscriminatedNode)discriminated;
    }

    private static void AssertUsesValue(MovRegImm16 node, ushort expectedValue) {
        Assert.True(node.ValueField.UseValue);
        Assert.Equal(expectedValue, node.ValueField.Value);
    }

    [AssertionMethod]
    private static MovRegImm16 AssertIsMovAx(ICfgNode node) {
        return AssertIsMovRegImm16(node, AxIndex);
    }

    [AssertionMethod]
    private static MovRegImm16 AssertIsMovBx(ICfgNode node) {
        return AssertIsMovRegImm16(node, BxIndex);
    }

    [AssertionMethod]
    private static MovRegImm16 AssertIsMovCx(ICfgNode node) {
        return AssertIsMovRegImm16(node, CxIndex);
    }

    [AssertionMethod]
    private static MovRegImm16 AssertIsMovRegImm16(ICfgNode node, int expectedRegIndex) {
        Assert.Equal(typeof(MovRegImm16), node.GetType());
        Assert.Equal(expectedRegIndex, ((MovRegImm16)node).RegisterIndex);
        return (MovRegImm16)node;
    }

    [AssertionMethod]
    private void AssertMovAxIsSameButValueFieldRefersMemory(ICfgNode movAx1, ICfgNode movAx1WithNullDiscriminator) {
        // Instructions instances are not necessarily the same. However, their types and addresses should be the same.
        // The value of the new instruction should be read from memory.
        Assert.Equal(movAx1.Address, movAx1WithNullDiscriminator.Address);
        MovRegImm16 movAx1RegImm16 = AssertIsMovAx(movAx1WithNullDiscriminator);
        // Since value has been overwritten use value should be false. Value need to be read from ram
        Assert.False(movAx1RegImm16.ValueField.UseValue);
        Assert.Null(movAx1RegImm16.ValueField.DiscriminatorValue[0]);
        Assert.Null(movAx1RegImm16.ValueField.DiscriminatorValue[1]);
    }
}