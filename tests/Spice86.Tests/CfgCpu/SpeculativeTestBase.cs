namespace Spice86.Tests.CfgCpu;

using NSubstitute;
using Microsoft.Extensions.Logging;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Logging;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System;

/// <summary>
/// Shared base class for speculative execution tests providing common infrastructure
/// (memory, state, parser, node index, linker, compiler) and utility helpers.
/// </summary>
public abstract class SpeculativeTestBase : IDisposable {
    private const byte Nop = 0x90;

    protected readonly Memory Memory;
    protected readonly AddressReadWriteBreakpoints MemoryBreakpoints;
    protected readonly State State;
    protected readonly InstructionParser Parser;
    protected readonly InstructionReplacerRegistry ReplacerRegistry;
    protected readonly CfgNodeIndex NodeIndex;
    protected readonly NodeLinker NodeLinker;
    protected readonly SequentialIdAllocator IdAllocator;
    protected readonly CfgNodeExecutionCompiler Compiler;
    protected readonly ILoggerService LoggerService;

    protected SpeculativeTestBase() {
        IdAllocator = new SequentialIdAllocator();
        MemoryBreakpoints = new AddressReadWriteBreakpoints();
        Memory = new Memory(MemoryBreakpoints, new Ram(0x100000), new A20Gate(), new RealModeMmu8086(), false);
        State = new State(CpuModel.INTEL_80286);
        Parser = new InstructionParser(Memory, State, IdAllocator);
        ReplacerRegistry = new InstructionReplacerRegistry();
        NodeIndex = new CfgNodeIndex(ReplacerRegistry);
        LoggerService = Substitute.For<LoggerService>();
        Compiler = new CfgNodeExecutionCompiler(new CfgNodeExecutionCompilerMonitor(LoggerService), LoggerService, JitMode.InterpretedOnly);
        NodeLinker = new NodeLinker(ReplacerRegistry, Compiler, IdAllocator);
    }

    protected CfgInstruction CreateSpeculativeNode(SegmentedAddress address) {
        uint physAddr = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset);
        Memory.UInt8[physAddr] = Nop;
        CfgInstruction node = Parser.ParseInstructionAt(address);
        node.SetSpeculative(true);
        NodeIndex.Insert(node);
        return node;
    }

    protected CfgInstruction CreateObservedNode(SegmentedAddress address) {
        uint physAddr = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset);
        Memory.UInt8[physAddr] = Nop;
        CfgInstruction node = Parser.ParseInstructionAt(address);
        NodeIndex.Insert(node);
        return node;
    }

    protected static void WireEdge(ICfgNode from, ICfgNode to) {
        from.Successors.Add(to);
        to.Predecessors.Add(from);
        SuccessorInvariant.Refresh(from);
        from.UpdateSuccessorCache();
    }

    protected void WriteNop(SegmentedAddress address) {
        uint physAddr = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset);
        Memory.UInt8[physAddr] = Nop;
    }

    protected void WriteRet(SegmentedAddress address) {
        uint physAddr = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset);
        Memory.UInt8[physAddr] = 0xC3;
    }

    protected void WriteJmpShort(SegmentedAddress address, sbyte relativeOffset) {
        uint physAddr = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset);
        Memory.UInt8[physAddr] = 0xEB;
        Memory.Int8[physAddr + 1] = relativeOffset;
    }

    protected CfgInstruction WriteNopAndParse(SegmentedAddress address) {
        uint physAddr = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset);
        Memory.UInt8[physAddr] = Nop;
        CfgInstruction instruction = Parser.ParseInstructionAt(address);
        return instruction;
    }

    protected CfgInstruction WriteConditionalJnzAndParse(SegmentedAddress address, sbyte relativeOffset) {
        uint physAddr = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset);
        Memory.UInt8[physAddr] = 0x75;
        Memory.Int8[physAddr + 1] = relativeOffset;
        CfgInstruction instruction = Parser.ParseInstructionAt(address);
        return instruction;
    }

    /// <summary>
    /// Builds a fully-wired <see cref="InstructionsFeeder"/> over the shared memory and state with
    /// speculative exploration enabled and a reachability pruner installed, mirroring the production
    /// wiring. Lets cold-path tests drive the real promote/discard flow without repeating the
    /// breakpoint/feeder/pruner bootstrap.
    /// </summary>
    protected InstructionsFeeder CreateSpeculativeFeeder() {
        AddressReadWriteBreakpoints ioBreakpoints = new();
        EmulatorBreakpointsManager breakpointsManager =
            new(new PauseHandler(LoggerService), State, Memory, MemoryBreakpoints, ioBreakpoints);
        InstructionsFeeder feeder = new(breakpointsManager, Memory, State, ReplacerRegistry,
            Compiler, IdAllocator, nodeLinker: NodeLinker);
        return feeder;
    }

    public virtual void Dispose() {
        Compiler.Dispose();
    }
}
