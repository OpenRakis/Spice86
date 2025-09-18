﻿using JetBrains.Annotations;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using Xunit;

namespace Spice86.Tests.CfgCpu;

using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.VM.Breakpoint;

public class CfgNodeFeederTest {
    private const int AxIndex = 0;
    private const int BxIndex = 3;
    private const int CxIndex = 1;
    private const ushort DefaultValue = 0xFFFF;
    private const ushort NewValue = 0x1234;
    private const int MovRegImm16Length = 3;
    private static readonly SegmentedAddress ZeroAddress = SegmentedAddress.ZERO;
    private static readonly SegmentedAddress EndOfMov0Address = new(0, MovRegImm16Length);

    private Memory _memory = new(new(), new Ram(64), new A20Gate());
    private State _state = new(CpuModel.INTEL_80286);

    private (CfgNodeFeeder, ExecutionContext) CreateCfgNodeFeeder() {
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        EmulatorBreakpointsManager emulatorBreakpointsManager = new EmulatorBreakpointsManager(new PauseHandler(loggerService), _state);
        _memory = new(emulatorBreakpointsManager.MemoryReadWriteBreakpoints, new Ram(64), new A20Gate());
        _state = new State(CpuModel.INTEL_80286);
        FunctionHandler functionHandler = new(_memory, _state, null, new(), false, loggerService);
        CfgNodeFeeder cfgNodeFeeder = new(_memory, _state, emulatorBreakpointsManager, new());
        ExecutionContext executionContext = new ExecutionContext(SegmentedAddress.ZERO, 0, functionHandler);
        return (cfgNodeFeeder, executionContext);
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
        (CfgNodeFeeder cfgNodeFeeder, ExecutionContext executionContext)  = CreateCfgNodeFeeder();
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
        (CfgNodeFeeder cfgNodeFeeder, ExecutionContext executionContext)  = CreateCfgNodeFeeder();
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
        (CfgNodeFeeder cfgNodeFeeder, ExecutionContext executionContext)  = CreateCfgNodeFeeder();
        WriteTwoMovAx();
        ICfgNode movAx0 = SimulateExecution(cfgNodeFeeder, executionContext);
        // Parse second Mov AX and insert it in graph
        ICfgNode movAx1 = cfgNodeFeeder.GetLinkedCfgNodeToExecute(executionContext);


        // Act
        // Some time later second Mov AX changes to Mov BX and we are about to execute it, graph says next is Mov AX but this is no the case.
        WriteMovBx(EndOfMov0Address, DefaultValue);
        executionContext.NodeToExecuteNextAccordingToGraph = movAx1;
        SelectorNode selectorNode = AssertIsSelectorNode(cfgNodeFeeder.GetLinkedCfgNodeToExecute(executionContext));

        // Assert
        // Mov AX0 is not anymore linked to mov ax1, there is a selector node between them.
        AssertDoesNotLinkTo(movAx0, movAx1);
        // Check the selector node is there
        AssertLinksTo(movAx0, selectorNode);
        // Check selector node also contains Mov BX
        ICfgNode movBx = selectorNode.Successors.First(node => !ReferenceEquals(node, movAx1));
        MovRegImm16 movBxRegImm16 = AssertIsMovBx(movBx);
        MovRegImm16 movAx1RegImm16 = AssertIsMovAx(movAx1);
        AssertUsesValue(movBxRegImm16, DefaultValue);
        AssertSuccessorAtSignature(selectorNode, movBxRegImm16);
        AssertSuccessorAtSignature(selectorNode, movAx1RegImm16);
    }

    [Fact]
    public void MovAxChangedValue() {
        // Arrange
        (CfgNodeFeeder cfgNodeFeeder, ExecutionContext executionContext)  = CreateCfgNodeFeeder();
        WriteTwoMovAx();
        SimulateExecution(cfgNodeFeeder, executionContext);
        // Just parse next and insert it in graph
        ICfgNode movAx1 = cfgNodeFeeder.GetLinkedCfgNodeToExecute(executionContext);

        // Act
        // We are still after movAx0 but it changed to MOV AX 1234.
        WriteMovAx(EndOfMov0Address, NewValue);
        // Execute changed instruction
        executionContext.NodeToExecuteNextAccordingToGraph = movAx1;
        ICfgNode movAx1WithNullSignature = cfgNodeFeeder.GetLinkedCfgNodeToExecute(executionContext);

        // Assert
        AssertMovAxIsSameButValueFieldRefersMemory(movAx1, movAx1WithNullSignature);
    }

    [Fact]
    public void MovAxChangedToMovBxThenMovCx() {
        // Arrange
        (CfgNodeFeeder cfgNodeFeeder, ExecutionContext executionContext)  = CreateCfgNodeFeeder();
        WriteTwoMovAx();
        SimulateExecution(cfgNodeFeeder, executionContext);
        ICfgNode movAx1 = cfgNodeFeeder.GetLinkedCfgNodeToExecute(executionContext);
        executionContext.NodeToExecuteNextAccordingToGraph = movAx1;
        WriteMovBx(EndOfMov0Address, DefaultValue);
        ICfgNode selector = cfgNodeFeeder.GetLinkedCfgNodeToExecute(executionContext);
        WriteMovCx(EndOfMov0Address, DefaultValue);
        // CPU executed signature but Mov CX was in memory => no successor of the signature matched
        executionContext.LastExecuted = selector;
        executionContext.NodeToExecuteNextAccordingToGraph = null;

        // Act
        // We at signature but instruction got changed to something that is not yet in the signature list of values.
        ICfgNode movCx = cfgNodeFeeder.GetLinkedCfgNodeToExecute(executionContext);

        // Assert
        SelectorNode selectorNode = AssertIsSelectorNode(selector);
        MovRegImm16 movCxRegImm16 = AssertIsMovCx(movCx);
        AssertSuccessorAtSignature(selectorNode, movCxRegImm16);
    }

    [Fact]
    public void MovAxChangedToMovBxThenMovAxWithDifferentValue() {
        // Arrange
        (CfgNodeFeeder cfgNodeFeeder, ExecutionContext executionContext)  = CreateCfgNodeFeeder();
        WriteTwoMovAx();
        SimulateExecution(cfgNodeFeeder, executionContext);
        ICfgNode movAx1 = cfgNodeFeeder.GetLinkedCfgNodeToExecute(executionContext);
        // Code goes back to mov AX1 but it has been changed to MOV BX, selector node should have been created
        executionContext.NodeToExecuteNextAccordingToGraph = movAx1;
        WriteMovBx(EndOfMov0Address, DefaultValue);
        ICfgNode selector = cfgNodeFeeder.GetLinkedCfgNodeToExecute(executionContext);
        // Then write MOV AX back but with a different value
        WriteMovAx(EndOfMov0Address, NewValue);
        // CPU executed selector node, and determined Mov AX was next according to graph since it was in memory => should match with initial mov AX
        executionContext.LastExecuted = selector;
        executionContext.NodeToExecuteNextAccordingToGraph = movAx1;

        // Act
        ICfgNode movAx1WithNullSignature = cfgNodeFeeder.GetLinkedCfgNodeToExecute(executionContext);

        // Assert
        AssertMovAxIsSameButValueFieldRefersMemory(movAx1, movAx1WithNullSignature);
    }

    [AssertionMethod]
    private static void AssertSuccessorAtSignature(SelectorNode predecessor, CfgInstruction expectedSuccessor) {
        AssertLinksTo(predecessor, expectedSuccessor);
        Assert.Equal(expectedSuccessor, predecessor.SuccessorsPerSignature[expectedSuccessor.Signature]);
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
    private static SelectorNode AssertIsSelectorNode(ICfgNode selector) {
        Assert.Equal(typeof(SelectorNode), selector.GetType());
        return (SelectorNode)selector;
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
    private void AssertMovAxIsSameButValueFieldRefersMemory(ICfgNode movAx1, ICfgNode movAx1WithNullSignature) {
        // Instructions instances are not necessarily the same. However, their types and addresses should be the same.
        // The value of the new instruction should be read from memory.
        Assert.Equal(movAx1.Address, movAx1WithNullSignature.Address);
        MovRegImm16 movAx1RegImm16 = AssertIsMovAx(movAx1WithNullSignature);
        // Since value has been overwritten use value should be false. Value need to be read from ram
        Assert.False(movAx1RegImm16.ValueField.UseValue);
        Assert.Null(movAx1RegImm16.ValueField.SignatureValue[0]);
        Assert.Null(movAx1RegImm16.ValueField.SignatureValue[1]);
    }
}