namespace Spice86.Tests.CfgCpu.Blocks;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CpuTests.SingleStepTests;

using ExecutionContext = Spice86.Core.Emulator.CPU.CfgCpu.Linker.ExecutionContext;

using System.Collections.Generic;
using System.Linq;

using static CfgTestHelpers;

using Xunit;

/// <summary>
/// Deterministic tests for the hot-path block walker in <see cref="CfgCpu.ExecuteBlock"/>.
/// Replaces the CsCheck-based CfgCpuHotPathPbt with explicit edge-case inputs.
/// </summary>
public class CfgCpuHotPathTest : IDisposable {
    private const ushort BaseSegment = 0x1000;
    private const int BlockSize = 4;
    private static readonly CfgNodeIdAllocator _allocator = new();
    private readonly SingleStepTestMinimalMachine _machine = new(CpuModel.INTEL_80286);

    private CfgCpu Cpu => _machine.Cpu;
    private State State => _machine.State;

    public void Dispose() {
        _machine.Dispose();
    }

    /// <summary>
    /// The hot path executes every instruction in a discovery-complete block exactly once
    /// and finishes at the terminator.
    /// </summary>
    [Fact]
    public void HotPath_ExecutesEntireBlock_ExactlyOnce() {
        ExecutionContext ctx = Cpu.ExecutionContextManager.CurrentExecutionContext;

        int executionCount = 0;
        (CfgBlock block, CfgInstruction[] instructions) = BuildBlock(BlockSize, (instr, i) => {
            instr.CompiledExecution = _ => executionCount++;
        });

        ctx.LastExecuted = null;
        ctx.NodeToExecuteNextAccordingToGraph = instructions[0];

        long cyclesBefore = State.Cycles;
        Cpu.ExecuteNext();
        long cyclesAfter = State.Cycles;

        executionCount.Should().Be(BlockSize,
            "every instruction must execute exactly once");
        (cyclesAfter - cyclesBefore).Should().Be(BlockSize,
            "each instruction increments cycles once");
        ctx.LastExecuted.Should().BeSameAs(block.Terminator,
            "walk must finish at the terminator");
    }

    /// <summary>
    /// When the graph points at an interior node of a discovered block, the executor must cold-step
    /// only that node rather than replaying the block prefix from the entry.
    /// </summary>
    [Fact]
    public void ExecuteNext_ColdSteps_WhenCompletedBlockNextNodeIsInterior() {
        ExecutionContext ctx = Cpu.ExecutionContextManager.CurrentExecutionContext;

        List<int> trace = new();
        (CfgBlock block, CfgInstruction[] instructions) = BuildBlock(BlockSize, (instr, i) => {
            int captured = i;
            instr.CompiledExecution = _ => trace.Add(captured);
        });

        ctx.LastExecuted = null;
        ctx.NodeToExecuteNextAccordingToGraph = instructions[2];

        long cyclesBefore = State.Cycles;
        Cpu.ExecuteNext();
        long cyclesAfter = State.Cycles;

        trace.Should().Equal([2],
            "an interior entry is outside the block hot-path contract");
        (cyclesAfter - cyclesBefore).Should().Be(1,
            "cold stepping executes one node");
        ctx.LastExecuted.Should().BeSameAs(instructions[2]);
        block.Entry.Should().BeSameAs(instructions[0]);
    }

    /// <summary>
    /// Incomplete blocks are still being discovered, so the executor must keep taking the cold path.
    /// </summary>
    [Fact]
    public void ExecuteNext_ColdSteps_WhenBlockDiscoveryIsIncomplete() {
        ExecutionContext ctx = Cpu.ExecutionContextManager.CurrentExecutionContext;
        List<int> trace = new();
        (CfgBlock block, CfgInstruction[] instructions) = BuildBlock(BlockSize, (instr, i) => {
            int captured = i;
            instr.CompiledExecution = _ => trace.Add(captured);
        });
        block.IsDiscoveryComplete = false;

        ctx.LastExecuted = null;
        ctx.NodeToExecuteNextAccordingToGraph = instructions[0];

        Cpu.ExecuteNext();

        trace.Should().Equal([0]);
        ctx.LastExecuted.Should().BeSameAs(instructions[0]);
    }

    /// <summary>
    /// Non-live blocks may contain stale compiled nodes, so entry dispatch must remain cold.
    /// </summary>
    [Fact]
    public void ExecuteNext_ColdSteps_WhenBlockIsNotLive() {
        ExecutionContext ctx = Cpu.ExecutionContextManager.CurrentExecutionContext;
        List<int> trace = new();
        (CfgBlock block, CfgInstruction[] instructions) = BuildBlock(BlockSize, (instr, i) => {
            int captured = i;
            instr.CompiledExecution = _ => trace.Add(captured);
        });
        instructions[1].SetLive(false);

        ctx.LastExecuted = null;
        ctx.NodeToExecuteNextAccordingToGraph = instructions[0];

        Cpu.ExecuteNext();

        block.IsLive.Should().BeFalse();
        trace.Should().Equal([0]);
        ctx.LastExecuted.Should().BeSameAs(instructions[0]);
    }

    /// <summary>
    /// The hot path stops before executing a non-live instruction.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void HotPath_StopsBeforeNonLiveInstruction(int nonLiveIndex) {
        List<int> trace = new();
        (CfgBlock block, CfgInstruction[] instructions) = BuildBlock(BlockSize, (instr, i) => {
            int captured = i;
            instr.CompiledExecution = _ => trace.Add(captured);
        });

        instructions[nonLiveIndex].SetLive(false);

        Cpu.ExecuteBlock(block);

        trace.Should().Equal(Enumerable.Range(0, nonLiveIndex),
            "hot path must stop before the non-live instruction");
    }

    /// <summary>
    /// The hot path stops after executing an instruction that throws CpuException.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void HotPath_StopsAfterCpuException(int faultIndex) {
        List<int> trace = new();
        (CfgBlock block, CfgInstruction[] instructions) = BuildBlock(BlockSize, (instr, i) => {
            int captured = i;
            bool shouldThrow = captured == faultIndex;
            instr.CompiledExecution = _ => {
                trace.Add(captured);
                if (shouldThrow) {
                    throw new CpuDivisionErrorException("test fault");
                }
            };
        });

        ICfgNode lastExecuted = Cpu.ExecuteBlock(block);

        trace.Should().Equal(Enumerable.Range(0, faultIndex + 1),
            "hot path must stop after the faulting instruction");
        lastExecuted.Should().BeSameAs(instructions[faultIndex]);
    }

    private static (CfgBlock Block, CfgInstruction[] Instructions) BuildBlock(int size, Action<CfgInstruction, int> configure) {
        CfgInstruction[] instructions = new CfgInstruction[size];
        for (int i = 0; i < size; i++) {
            CfgInstruction instr = CreateInstruction(new SegmentedAddress(BaseSegment, (ushort)i));
            configure(instr, i);
            instructions[i] = instr;
        }
        CfgBlock block = new(_allocator.AllocateId(), instructions[0]);
        for (int i = 1; i < size; i++) {
            block.Append(instructions[i]);
        }
        foreach (CfgInstruction instr in instructions) {
            instr.ContainingBlock = block;
        }
        block.IsDiscoveryComplete = true;
        return (block, instructions);
    }

}

