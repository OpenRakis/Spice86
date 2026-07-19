namespace Spice86.Tests.CfgCpu;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;

using Xunit;

/// <summary>
/// Graph-level backstop for the dangling-generation-root fix: a seeded generation-root entry point
/// that is swept while still speculative must be dropped from
/// <see cref="ExecutionContextManager.ExecutionContextEntryPoints"/> and from the node index, leaving
/// no detached, de-indexed ghost lingering as a dead generation root. This pins the fix at the unit
/// boundary independent of emulator handler layout.
/// </summary>
public sealed class SpeculativeSweepEntryPointTest : SpeculativeTestBase {
    private readonly SpeculativeReachabilityPruner _pruner;
    private readonly ExecutionContextManager _contextManager;

    public SpeculativeSweepEntryPointTest() {
        _pruner = new SpeculativeReachabilityPruner(ReplacerRegistry);
        _contextManager = new ExecutionContextManager(Memory, State, BuildIsolatedFeeder(), ReplacerRegistry,
            new FunctionCatalogue(), false, LoggerService, null);
    }

    // The feeder is built on its own throwaway registry so the only subscriber joining the shared
    // ReplacerRegistry the pruner fans out through is the ExecutionContextManager under test (plus the
    // base index/linker). Otherwise the feeder's own caches would double-register.
    private CfgNodeFeeder BuildIsolatedFeeder() {
        InstructionReplacerRegistry isolatedRegistry = new();
        EmulatorBreakpointsManager breakpointsManager = new(new PauseHandler(LoggerService), State, Memory,
            MemoryBreakpoints, new AddressReadWriteBreakpoints());
        return new CfgNodeFeeder(Memory, State, breakpointsManager, isolatedRegistry, Compiler, IdAllocator,
            enableSpeculativeExploration: false);
    }

    /// <summary>
    /// A seeded speculative entry point swept before it fires must not survive as a dead root.
    /// Arrange: a speculative node registered as a CFG generation root.
    /// Act: Sweep the node.
    /// Assert: the entry-point map no longer references it and the index no longer contains it.
    /// </summary>
    [Fact]
    public void SweepingSeededSpeculativeEntryPointDropsItAsGenerationRoot() {
        SegmentedAddress address = new(0, 0x100);
        CfgInstruction seeded = CreateSpeculativeNode(address);
        _contextManager.RegisterEntryPoint(seeded);
        _contextManager.ExecutionContextEntryPoints.Should().ContainKey(address, "sanity: the root was seeded");

        _pruner.Sweep(seeded);

        _contextManager.ExecutionContextEntryPoints.Should().NotContainKey(address,
            "the swept seeded root must not linger as a detached, de-indexed dead generation root");
        NodeIndex.HasAddress(address).Should().BeFalse("the swept node must be de-indexed");
    }
}
