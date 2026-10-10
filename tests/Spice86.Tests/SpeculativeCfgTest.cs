namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;

using Spice86.Tests.AsmFixtures;

using System.Collections.Generic;
using System.Linq;

using Xunit;

/// <summary>
/// Tests for the Speculative CFG Exploration feature.
/// These validate that the explorer, promoter, generator, and runtime guard work end-to-end.
/// </summary>
public sealed class SpeculativeCfgTest {
    /// <summary>
    /// The generated code must handle both branches from a single discovery run.
    ///
    /// Discovery run observes only selector=0 (fallthrough path). The generated code must emit
    /// a guarded speculative branch for the unobserved arm (selector=1). When run with selector=1,
    /// the generated override itself must execute the speculative path and produce the correct result
    /// without falling back to the interpreter.
    ///
    /// This test performs a SINGLE discovery (selector=0), generates code from that trace, then
    /// runs the compiled override with selector=1. If the emitter still produces FailAsUntested
    /// instead of a guarded goto, this test will fail.
    /// </summary>
    [Fact]
    public void SpeculativeBranchExecutesCorrectlyOnBothPaths() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(
            AsmFixtureCatalog.SpeculativeBranch(SpeculativeArm.Observed),
            [AsmFixtureCatalog.SpeculativeBranch(SpeculativeArm.Observed),
             AsmFixtureCatalog.SpeculativeBranch(SpeculativeArm.Unobserved)]);
    }

    /// <summary>
    /// Regression for the cross-variant convergence leak: running the self-modifying fixture with
    /// speculation on must not introduce a SelectorNode at F000:1403. The three opcodes that occupy
    /// that address over time (push AX / shl BX / inc CX) are reached on distinct predecessor edges,
    /// so the observed-only graph has no selector there. Speculation must converge only on matching
    /// instructions (same final-field signature), never wiring a predecessor to an unrelated variant
    /// that would later force a selector during execution-time reconciliation.
    /// </summary>
    [Fact]
    public void SpeculationOnSelfModifyInstructionsCreatesNoSelectorAtCrossVariantAddress() {
        using Spice86Creator creator = new(binName: "selfmodifyinstructions", maxCycles: 100000,
            enableSpeculativeCfgExploration: true);
        using Spice86DependencyInjection di = creator.Create();
        di.ProgramExecutor.Run();
        Machine machine = di.Machine;

        ISet<ICfgNode> allNodes = BrowseAllReachableNodes(machine);

        uint linearOf1403 = 0xF0000 + 0x1403;
        bool selectorAt1403 = allNodes.Any(node => node is SelectorNode && node.Address.Linear == linearOf1403);
        selectorAt1403.Should().BeFalse(
            "speculation must not wire cross-variant convergence edges that force a SelectorNode at F000:1403");

        // The three distinct opcodes must still all be present at that address as separate variants.
        int instructionsAt1403 = allNodes
            .OfType<CfgInstruction>()
            .Count(node => node.Address.Linear == linearOf1403);
        instructionsAt1403.Should().Be(3,
            "push AX, shl BX and inc CX should each exist as their own variant at F000:1403");
    }

    private static ISet<ICfgNode> BrowseAllReachableNodes(Machine machine) {
        IEnumerable<CfgInstruction> entryPoints = machine.CfgCpu.ExecutionContextManager
            .ExecutionContextEntryPoints.Values.SelectMany(nodes => nodes);
        Queue<ICfgNode> queue = new(entryPoints);
        HashSet<ICfgNode> visited = new();
        while (queue.Count > 0) {
            ICfgNode node = queue.Dequeue();
            if (!visited.Add(node)) {
                continue;
            }
            foreach (ICfgNode successor in node.Successors) {
                queue.Enqueue(successor);
            }
            foreach (ICfgNode predecessor in node.Predecessors) {
                queue.Enqueue(predecessor);
            }
        }
        return visited;
    }

    /// <summary>
    /// Recursive closure (multi-block unobserved arm).
    /// A single discovery run (selector=0) observes only the fallthrough. The generated code must
    /// emit a guarded speculative closure for the loop path (selector=1). Running the same generated
    /// code with selector=1 must execute the speculative loop and produce the correct result.
    /// </summary>
    [Fact]
    public void SpeculativeClosureMultiBlockLoopExecutesCorrectly() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(
            AsmFixtureCatalog.SpeculativeClosure(SpeculativeArm.Observed),
            [AsmFixtureCatalog.SpeculativeClosure(SpeculativeArm.Observed),
             AsmFixtureCatalog.SpeculativeClosure(SpeculativeArm.Unobserved)]);
    }

    /// <summary>
    /// Poison set persistence: after a dump-then-reload cycle the poison set is preserved.
    /// </summary>
    [Fact]
    public void PoisonSetSurvivedDumpReloadCycle() {
        // Run a program to build a graph, manually poison an address, dump, reload, and verify.
        using Spice86Creator discoveryCreator = new(binName: "add", maxCycles: 1000);
        using Spice86DependencyInjection discoveryDi = discoveryCreator.Create();
        discoveryDi.ProgramExecutor.Run();

        Machine discoveryMachine = discoveryDi.Machine;
        CfgNodeIndex nodeIndex = discoveryMachine.CfgCpu.CfgNodeFeeder.NodeIndex;

        // Manually poison an address
        Spice86.Shared.Emulator.Memory.SegmentedAddress poisonedAddr = new(0x0170, 0x0050);
        nodeIndex.PoisonSet.Add(poisonedAddr);
        nodeIndex.PoisonSet.Should().Contain(poisonedAddr);

        // Export and reload
        Spice86.Core.Emulator.StateSerialization.CfgReload.CfgReloadDump dump =
            new Spice86.Core.Emulator.StateSerialization.CfgReload.CfgReloadExporter()
                .Export(discoveryMachine.CfgCpu.ExecutionContextManager, nodeIndex.PoisonSet);

        dump.PoisonedAddresses.Should().NotBeNull();
        dump.PoisonedAddresses.Should().Contain(poisonedAddr.ToString());

        // Verify it round-trips through JSON
        string json = System.Text.Json.JsonSerializer.Serialize(dump,
            Spice86.Core.Emulator.StateSerialization.CfgReload.CfgReloadSerialization.Options);
        Spice86.Core.Emulator.StateSerialization.CfgReload.CfgReloadDump reloaded =
            System.Text.Json.JsonSerializer.Deserialize<Spice86.Core.Emulator.StateSerialization.CfgReload.CfgReloadDump>(json,
                Spice86.Core.Emulator.StateSerialization.CfgReload.CfgReloadSerialization.Options)
            ?? throw new InvalidOperationException("the dump must deserialize back into a non-null CfgReloadDump");

        reloaded.PoisonedAddresses.Should().Contain(poisonedAddr.ToString());
    }

    /// <summary>
    /// Convergence onto observed code - both paths reach the same merge point.
    /// Discovery (selector=0) observes the mergepoint via path A. Speculative path B (selector=1)
    /// converges onto the same observed mergepoint block. No duplicate, just a goto.
    /// </summary>
    [Fact]
    public void SpeculativeConvergenceOntoObservedCodeExecutesCorrectly() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(
            AsmFixtureCatalog.SpeculativeConvergence(SpeculativeArm.Observed),
            [AsmFixtureCatalog.SpeculativeConvergence(SpeculativeArm.Observed),
             AsmFixtureCatalog.SpeculativeConvergence(SpeculativeArm.Unobserved)]);
    }

    /// <summary>
    /// Mixed-block guard placement. The generated code for the speculative fallthrough arm
    /// must include a guard and execute correctly on both paths.
    /// </summary>
    [Fact]
    public void SpeculativeMixedBlockGuardPlacement() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(
            AsmFixtureCatalog.SpeculativeMixedBlock(SpeculativeArm.Observed),
            [AsmFixtureCatalog.SpeculativeMixedBlock(SpeculativeArm.Observed),
             AsmFixtureCatalog.SpeculativeMixedBlock(SpeculativeArm.Unobserved)]);
    }

    /// <summary>
    /// Mid-block self-modifying code inside a speculative run must be caught.
    ///
    /// Discovery observes only the fallthrough path (selector=0). On the speculative path
    /// (selector=1) the speculative block holds two instructions in the same straight-line run:
    /// the first rewrites the ModRM byte of the second through a CS-segment-override write, so the
    /// second instruction's bytes no longer match what the explorer decoded and baked into the
    /// generated body. A single block-entry guard cannot catch this because at block entry the
    /// bytes still match; the divergence only appears after the earlier instruction has executed.
    /// Only a guard emitted before each speculative instruction detects it.
    ///
    /// Running the override compiled from the selector=0 trace with selector=1 must therefore throw
    /// (the guard before the modified instruction fires) instead of silently executing the stale
    /// decode-time body. No external memory modification is performed: the SMC is done by the
    /// program itself, in-block.
    /// </summary>
    [Fact]
    public void SpeculativeSmcGuardMidBlockSelfModificationGuardFires() {
        AsmFixture fixture = AsmFixtureCatalog.SpeculativeSmcGuard();
        GeneratedCodeMachineTestRunner runner = new();
        GeneratedCSharpProgram generatedProgram = runner.GenerateProgramAndSource(fixture.Settings);
        using CompiledGeneratedOverride compiledOverride = runner.CompileGeneratedProgram(fixture.Settings, generatedProgram);

        // Discovery path (selector=0): no SMC on this path, runs to completion.
        AsmFixtureRunner.RunWithOverride(fixture, compiledOverride.Supplier).Dispose();

        // Speculative path (selector=1): the first speculative instruction (F000:0013) rewrites the
        // ModRM byte of the second speculative instruction (F000:0019) in the same block. The guard
        // emitted before that second instruction detects the divergence and throws. The fired guard
        // is the one at F000:0019, not the block-entry guard at F000:0013, which proves the
        // detection happens mid-block rather than only at entry.
        AsmRunSettings unobservedSettings = fixture.Settings with { ConfigureMachine = AsmFixtureCatalog.SelectUnobservedArm };
        Action act = () => {
            using AsmFixtureRun run = AsmFixtureRunner.RunSettingsWithOverride(unobservedSettings, compiledOverride.Supplier);
        };

        act.Should().Throw<Spice86.Core.Emulator.Errors.InvalidVMOperationException>()
            .WithInnerException<Spice86.Shared.Emulator.Errors.UnrecoverableException>()
            .WithMessage("*Speculative code at F000:0019 no longer matches memory*");

        runner.CompareGeneratedSourceWithExpected(
            GeneratedCodeMachineTestRunner.GoldenKey(fixture.Settings), generatedProgram);
    }

    /// <summary>
    /// Discard-on-divergence. Generated code with speculative path B is run after the memory
    /// at B's target has been modified. The VerifySpeculativeEntryOrFail guard fires (memory changed)
    /// and throws UnrecoverableException, preventing silent wrong execution.
    /// </summary>
    [Fact]
    public void SpeculativeDiscardOnDivergenceGuardFires() {
        AsmFixture fixture = AsmFixtureCatalog.SpeculativeDiscard();
        GeneratedCodeMachineTestRunner runner = new();
        GeneratedCSharpProgram generatedProgram = runner.GenerateProgramAndSource(fixture.Settings);
        using CompiledGeneratedOverride compiledOverride = runner.CompileGeneratedProgram(fixture.Settings, generatedProgram);

        // Discovery path (selector=0): works fine
        AsmFixtureRunner.RunWithOverride(fixture, compiledOverride.Supplier).Dispose();

        // Speculative path (selector=1) with memory modification: guard fires and throws.
        // This proves the guard detects byte-level divergence and prevents silent wrong execution.
        AsmRunSettings unobservedSettings = fixture.Settings with {
            ConfigureMachine = machine => {
                machine.Memory.UInt8[0, 0x0500] = 0x01;
                // Modify the immediate byte at F000:0017 from 0xEE to 0xEF
                machine.Memory.UInt8[0xF000, 0x0017] = 0xEF;
            }
        };
        Action act = () => {
            using AsmFixtureRun run = AsmFixtureRunner.RunSettingsWithOverride(unobservedSettings, compiledOverride.Supplier);
        };

        act.Should().Throw<Spice86.Core.Emulator.Errors.InvalidVMOperationException>()
            .WithInnerException<Spice86.Shared.Emulator.Errors.UnrecoverableException>()
            .WithMessage("*Speculative code at F000:0013 no longer matches memory*");

        runner.CompareGeneratedSourceWithExpected(
            GeneratedCodeMachineTestRunner.GoldenKey(fixture.Settings), generatedProgram);
    }

    /// <summary>
    /// Known-safe handler seeding: after the emulator installs its provided interrupt handlers,
    /// every emulator-installed hardware IRQ handler must be registered as a CFG generation root (an
    /// execution-context entry point) so the handler flows into code generation even when no IRQ
    /// fired during discovery. This is the mechanism that prevents the
    /// "Could not find an override at address F000:xxxx" crash in generated code.
    ///
    /// Verified independently of the production seeding path: expectations are the fixed BIOS-default
    /// vectors of the hardware IRQ handlers the emulator installs (timer/keyboard/RTC/mouse plus the
    /// DefaultIrqHandler lines 3,4,5,7,10,11), not the PIC's runtime vector enumeration. Software
    /// interrupt handlers (reached from the program's own INT instructions) must NOT be seeded.
    /// </summary>
    [Fact]
    public void ExternalEventHandlersRegisteredAsGenerationRoots() {
        // BIOS-default vectors of the emulator-installed hardware IRQ handlers.
        // IRQ0->0x08 (timer), IRQ1->0x09 (keyboard), IRQ8->0x70 (RTC), IRQ12->0x74 (mouse),
        // DefaultIrqHandler IRQ3/4/5/7->0x0B/0x0C/0x0D/0x0F, IRQ10/11->0x72/0x73.
        byte[] hardwareInterruptVectors = [0x08, 0x09, 0x0B, 0x0C, 0x0D, 0x0F, 0x70, 0x72, 0x73, 0x74];
        // Software interrupt handlers: serviced via the program's INT instructions, never seeded.
        byte[] softwareInterruptVectors = [0x10, 0x16, 0x1C];

        using Spice86Creator creator = new(binName: "add", maxCycles: 1000,
            installInterruptVectors: true, enableSpeculativeCfgExploration: true);
        using Spice86DependencyInjection di = creator.Create();

        Machine machine = di.Machine;
        ExecutionContextManager contextManager = machine.CfgCpu.ExecutionContextManager;

        foreach (byte vectorNumber in hardwareInterruptVectors) {
            SegmentedAddress handlerAddress = machine.InterruptVectorTable[vectorNumber];
            handlerAddress.Should().NotBe(SegmentedAddress.ZERO,
                $"the emulator must install a hardware IRQ handler at vector 0x{vectorNumber:X2}");
            contextManager.ExecutionContextEntryPoints.Should().ContainKey(handlerAddress,
                $"hardware IRQ handler at vector 0x{vectorNumber:X2} ({handlerAddress}) must be a generation root");
        }

        foreach (byte vectorNumber in softwareInterruptVectors) {
            SegmentedAddress handlerAddress = machine.InterruptVectorTable[vectorNumber];
            contextManager.ExecutionContextEntryPoints.Should().NotContainKey(handlerAddress,
                $"software interrupt handler at vector 0x{vectorNumber:X2} ({handlerAddress}) must not be a generation root");
        }
    }

    /// <summary>
    /// End-to-end guard for the dangling-generation-root fix. With interrupt vectors installed and
    /// speculation on, the emulator seeds the INT 8 (IRQ0 / PIT) handler as a speculative CFG
    /// generation root. The fixture patches a different ISR at that address and lets the timer fire,
    /// so the first INT 8 reconciles to a signature mismatch and SWEEPS the still-speculative seeded
    /// root. The sweep must route through the RemoveInstruction fan-out so the root is dropped from
    /// both the node index and the entry-point set: no detached, de-indexed ghost may survive as a
    /// dead generation root. Reaching the end of Run (rather than the cycle-limit failsafe) proves the
    /// handler fired and the reconcile/sweep ran.
    /// </summary>
    [Fact]
    public void SeededTimerHandlerSweptDuringReconciliationLeavesNoDanglingRoot() {
        using Spice86Creator creator = new(binName: "speculative_seeded_timer_sweep",
            maxCycles: 0xFFFFFFF, enablePit: true, installInterruptVectors: true,
            enableSpeculativeCfgExploration: true);
        using Spice86DependencyInjection di = creator.Create();
        Machine machine = di.Machine;

        SegmentedAddress timerHandlerAddress = machine.InterruptVectorTable[0x8];

        di.ProgramExecutor.Run();

        CfgNodeIndex nodeIndex = machine.CfgCpu.CfgNodeFeeder.NodeIndex;
        ExecutionContextManager contextManager = machine.CfgCpu.ExecutionContextManager;

        nodeIndex.PoisonSet.Should().Contain(timerHandlerAddress,
            "the first INT 8 must reconcile to a signature mismatch and poison the seeded handler address");

        foreach (KeyValuePair<SegmentedAddress, ISet<CfgInstruction>> entry in contextManager.ExecutionContextEntryPoints) {
            foreach (CfgInstruction root in entry.Value) {
                nodeIndex.GetAtAddress(entry.Key).Should().Contain(root,
                    $"generation root at {entry.Key} must still be indexed - a swept root must leave no detached ghost");
            }
        }
    }

    /// <summary>
    /// With speculation off, the hardware IRQ handlers must NOT be registered as generation roots
    /// (no seeded node is produced), keeping behavior identical to before the feature.
    /// </summary>
    [Fact]
    public void ExternalEventHandlersNotRegisteredWhenSpeculationDisabled() {
        byte[] hardwareInterruptVectors = [0x08, 0x09, 0x0B, 0x0C, 0x0D, 0x0F, 0x70, 0x72, 0x73, 0x74];

        using Spice86Creator creator = new(binName: "add", maxCycles: 1000,
            installInterruptVectors: true, enableSpeculativeCfgExploration: false);
        using Spice86DependencyInjection di = creator.Create();

        Machine machine = di.Machine;
        ExecutionContextManager contextManager = machine.CfgCpu.ExecutionContextManager;

        foreach (byte vectorNumber in hardwareInterruptVectors) {
            SegmentedAddress handlerAddress = machine.InterruptVectorTable[vectorNumber];
            contextManager.ExecutionContextEntryPoints.Should().NotContainKey(handlerAddress,
                "with speculation disabled no handler should be registered as a generation root");
        }
    }

    /// <summary>
    /// End-to-end: with DOS initialized and speculation on, the generated code for
    /// intchain.com (which exercises INT chains through F000 handlers) compiles AND runs
    /// successfully as an override. This proves seeded handlers reach generation and execute.
    /// The run checks the BIOS tick counter: the INT 8 handler increments it once.
    /// </summary>
    [Fact]
    public void ExternalEventHandlerGeneratedOverrideCompilesAndRuns() {
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmFixtureCatalog.IntChain());
    }

    /// <summary>
    /// The single speculation-off scenario: it proves why speculation is enabled everywhere else.
    ///
    /// Discovery observes only the fallthrough arm (selector=0). With speculation off the unobserved
    /// JNZ arm is never explored, so the generator emits FailAsUntested for it instead of a guarded
    /// speculative branch. The `speculative_branch` golden without suffix shows that FailAsUntested on
    /// the unobserved arm; the runtime throw below proves the behavior. The override therefore runs
    /// correctly on the observed path but, when run with selector=1 (the path speculative discovery
    /// would have explored), reaches the untested arm and throws. This is the missing-node crash that
    /// speculative discovery prevents.
    /// </summary>
    [Fact]
    public void SpeculationOffLeavesUnobservedArmUntestedAndCrashesOnThatPath() {
        AsmFixture fixture = AsmFixtureCatalog.SpeculativeBranchWithoutSpeculation();
        GeneratedCodeMachineTestRunner runner = new();
        GeneratedCSharpProgram generatedProgram = runner.GenerateProgramAndSource(fixture.Settings);
        using CompiledGeneratedOverride compiledOverride = runner.CompileGeneratedProgram(fixture.Settings, generatedProgram);

        // Observed path (selector=0): runs to the correct result.
        AsmFixtureRunner.RunWithOverride(fixture, compiledOverride.Supplier).Dispose();

        // Unobserved path (selector=1): the untested arm is reached and the generated code throws,
        // proving the speculative node omitted by discovery is actually needed at runtime.
        AsmRunSettings unobservedSettings = fixture.Settings with { ConfigureMachine = AsmFixtureCatalog.SelectUnobservedArm };
        Action act = () => {
            using AsmFixtureRun run = AsmFixtureRunner.RunSettingsWithOverride(unobservedSettings, compiledOverride.Supplier);
        };

        act.Should().Throw<Spice86.Core.Emulator.Errors.InvalidVMOperationException>()
            .WithInnerException<Spice86.Shared.Emulator.Errors.UnrecoverableException>()
            .WithMessage("*Untested code reached*");

        runner.CompareGeneratedSourceWithExpected(GeneratedCodeMachineTestRunner.GoldenKey(fixture.Settings), generatedProgram);
    }
}
