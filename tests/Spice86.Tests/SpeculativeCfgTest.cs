namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;

using System.Collections.Generic;
using System.IO;
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
        // Single discovery run with selector=0 (only observes fallthrough path)
        GeneratedCodeRunOptions discoveryOptions = new() {
            MaxCycles = 1000,
            EnableSpeculativeCfgExploration = true
        };
        GeneratedCodeMachineTestRunner runner = new();
        (_, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("speculative_branch", discoveryOptions);
        string source = generatedProgram.SourceText;

        // Assert the generated source emits a speculative guard, NOT FailAsUntested,
        // for the unobserved conditional jump target (F000:000A -> F000:0013).
        source.Should().Contain("VerifySpeculativeEntryOrFail",
            "the emitter must produce a speculative guard for the unobserved JNZ arm, not FailAsUntested");
        source.Should().NotContain("Unobserved conditional jump target at F000:000A",
            "the speculative block at F000:0013 is reachable from the observed conditional and must not be treated as untested");

        // Compile the override generated from selector=0 discovery
        CompiledGeneratedOverride compiledOverride = new GeneratedOverrideCompiler().CompileSupplier(source);

        // Run with selector=0 (observed path) - should produce 0xDD
        byte[] expectedDiscovery = new byte[0x403];
        expectedDiscovery[0x400] = 0x01;
        expectedDiscovery[0x401] = 0xDD;
        expectedDiscovery[0x402] = 0xAA;

        RunWithCompiledOverride("speculative_branch", compiledOverride, expectedDiscovery, 1000);

        // Run with selector=1 (speculative path) using the SAME generated code
        // The override must handle this path via the guarded speculative branch.
        byte[] expectedSpeculative = new byte[0x403];
        expectedSpeculative[0x400] = 0x01;
        expectedSpeculative[0x401] = 0xEE;
        expectedSpeculative[0x402] = 0xAA;

        RunWithCompiledOverride("speculative_branch", compiledOverride, expectedSpeculative, 1000,
            machine => { machine.Memory.UInt8[0, 0x0500] = 0x01; });
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
    /// Source-level: the speculative branch fixture compiles and runs successfully via the
    /// generated override on the observed path.
    /// </summary>
    [Fact]
    public void SpeculativeBranchGeneratedOverrideCompilesAndRuns() {
        byte[] expectedDiscovery = new byte[0x403];
        expectedDiscovery[0x400] = 0x01;
        expectedDiscovery[0x401] = 0xDD;
        expectedDiscovery[0x402] = 0xAA;
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("speculative_branch", expectedDiscovery,
            new GeneratedCodeRunOptions { MaxCycles = 1000 });
    }

    /// <summary>
    /// Flag-off parity / speculation-on behavior for jump1.
    /// With speculation enabled (default), jump1's unobserved fallthrough paths are resolved
    /// via same-partition block entry lookup (speculative blocks are reachable from observed terminators).
    /// </summary>
    [Fact]
    public void SpeculationOnJump1ResolvesUnobservedFallthroughs() {
        GeneratedCodeRunOptions options = new() {
            MaxCycles = 1000,
            EnableSpeculativeCfgExploration = true
        };
        GeneratedCodeMachineTestRunner runner = new();
        (_, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("jump1", options);
        string source = generatedProgram.SourceText;

        // jump1's always-taken conditional jumps leave their fallthrough edge unobserved. With
        // speculation off those edges lower to FailAsUntested guarded by an "Unobserved conditional
        // fallthrough" comment. With speculation on, every such fallthrough target is a block entry
        // reachable from an observed terminator in the same partition, so it must be resolved rather
        // than left untested.
        source.Should().NotContain("Unobserved conditional fallthrough",
            "speculation must resolve jump1's unobserved fallthrough edges to their same-partition block entries");
        source.Should().NotContain("FailAsUntested",
            "no jump1 arm should remain untested once speculation resolves the fallthrough edges");

        // The resolved code must still compile and execute to the correct result via the override.
        string memoryDumpPath = "Resources/cpuTests/res/MemoryDumps/jump1.bin";
        byte[] expected = File.Exists(memoryDumpPath) ? File.ReadAllBytes(memoryDumpPath) : [];
        runner.TestGeneratedCode("jump1", expected, options);
    }

    /// <summary>
    /// Recursive closure (multi-block unobserved arm).
    /// A single discovery run (selector=0) observes only the fallthrough. The generated code must
    /// emit a guarded speculative closure for the loop path (selector=1). Running the same generated
    /// code with selector=1 must execute the speculative loop and produce the correct result.
    /// </summary>
    [Fact]
    public void SpeculativeClosureMultiBlockLoopExecutesCorrectly() {
        // Single discovery run with selector=0 (only observes fallthrough)
        GeneratedCodeRunOptions discoveryOptions = new() {
            MaxCycles = 1000,
            EnableSpeculativeCfgExploration = true
        };
        GeneratedCodeMachineTestRunner runner = new();
        (_, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("speculative_closure", discoveryOptions);
        string source = generatedProgram.SourceText;

        // Assert speculative guard is emitted for the closure entry
        source.Should().Contain("VerifySpeculativeEntryOrFail",
            "the emitter must produce a speculative guard for the unobserved JNZ arm leading to the loop closure");
        source.Should().NotContain("Unobserved conditional jump target",
            "the speculative closure must be resolved, not treated as untested");

        // Compile the override generated from selector=0 discovery
        CompiledGeneratedOverride compiledOverride = new GeneratedOverrideCompiler().CompileSupplier(source);

        // Run with selector=0 (observed path) - should produce 0xDD
        byte[] expectedDiscovery = new byte[0x403];
        expectedDiscovery[0x400] = 0x01;
        expectedDiscovery[0x401] = 0xDD;
        expectedDiscovery[0x402] = 0xBB;

        RunWithCompiledOverride("speculative_closure", compiledOverride, expectedDiscovery, 1000);

        // Run with selector=1 (speculative loop path) using the SAME generated code
        byte[] expectedSpeculative = new byte[0x403];
        expectedSpeculative[0x400] = 0x01;
        expectedSpeculative[0x401] = 0x03;
        expectedSpeculative[0x402] = 0xBB;

        RunWithCompiledOverride("speculative_closure", compiledOverride, expectedSpeculative, 1000,
            machine => { machine.Memory.UInt8[0, 0x0500] = 0x01; });
    }

    /// <summary>
    /// Source-level: the speculative closure fixture compiles and runs successfully on the observed path.
    /// </summary>
    [Fact]
    public void SpeculativeClosureGeneratedOverrideCompilesAndRuns() {
        byte[] expectedDiscovery = new byte[0x403];
        expectedDiscovery[0x400] = 0x01;
        expectedDiscovery[0x401] = 0xDD;
        expectedDiscovery[0x402] = 0xBB;
        new GeneratedCodeMachineTestRunner().TestGeneratedCode("speculative_closure", expectedDiscovery,
            new GeneratedCodeRunOptions { MaxCycles = 1000 });
    }

    /// <summary>
    /// Hard-stop on indirect transfer. The 'segpr' fixture has a direct call whose callee never
    /// returns (no observed continuation). With speculation on, the call continuation is still NOT
    /// speculated (it's out of scope by design). The generated source must still contain
    /// "no continuation was observed during discovery" for that call.
    /// </summary>
    [Fact]
    public void CallContinuationStaysOutOfScope() {
        GeneratedCodeMachineTestRunner runner = new();
        (_, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("segpr", maxCycles: 10000);
        string source = generatedProgram.SourceText;

        source.Should().Contain("but no continuation was observed during discovery.");
    }

    /// <summary>
    /// Mixed-block guard placed mid-block after observed prefix.
    /// The speculative_branch fixture contains a conditional where the fallthrough is observed and
    /// the JNZ target is speculative. The block containing the JNZ arm starts with the speculative
    /// instruction. The guard must appear BEFORE the first speculative instruction's generated comment.
    /// </summary>
    [Fact]
    public void MixedBlockGuardPlacedBeforeFirstSpeculativeInstruction() {
        GeneratedCodeRunOptions options = new() {
            MaxCycles = 1000,
            EnableSpeculativeCfgExploration = true
        };
        GeneratedCodeMachineTestRunner runner = new();
        (_, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("speculative_branch", options);
        string source = generatedProgram.SourceText;

        // The guard must appear in the generated source
        source.Should().Contain("VerifySpeculativeEntryOrFail(");

        // The guard must come BEFORE the first speculative block's instructions in the source
        int guardIndex = source.IndexOf("VerifySpeculativeEntryOrFail(", StringComparison.Ordinal);
        // F000:0013 is the alt_path entry (speculative JNZ target)
        int speculativeInstructionIndex = source.IndexOf("// F000:0013", StringComparison.Ordinal);
        speculativeInstructionIndex.Should().BeGreaterThanOrEqualTo(0,
            "the first speculative instruction comment (F000:0013) must be present in the generated source");
        guardIndex.Should().BeLessThan(speculativeInstructionIndex,
            "the speculative guard must be emitted before the first speculative instruction comment");
    }

    /// <summary>
    /// Flag-off produces FailAsUntested, no guard.
    /// With EnableSpeculativeCfgExploration = false, the generated code should NOT contain
    /// VerifySpeculativeEntryOrFail and should still contain FailAsUntested for unobserved arms.
    /// </summary>
    [Fact]
    public void FlagOffProducesFailAsUntestedNoGuard() {
        GeneratedCodeRunOptions options = new() {
            MaxCycles = 1000,
            EnableSpeculativeCfgExploration = false
        };
        GeneratedCodeMachineTestRunner runner = new();
        (_, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("speculative_branch", options);
        string source = generatedProgram.SourceText;

        source.Should().NotContain("VerifySpeculativeEntryOrFail",
            "with speculation disabled, no speculative guards should be emitted");
        source.Should().Contain("FailAsUntested",
            "with speculation disabled, unobserved arms must still produce FailAsUntested");
    }

    /// <summary>
    /// Convergence onto observed block emits plain goto, no guard.
    /// When a speculative arm targets a block that was already observed (in the same partition),
    /// the generated code should emit a plain goto to that label, not a VerifySpeculativeEntryOrFail guard.
    /// The observed target is already confirmed by execution - no verification needed.
    /// </summary>
    [Fact]
    public void ConvergenceOntoObservedBlockEmitsPlainGotoNoGuard() {
        // The speculative arm of speculative_branch converges onto the 'done' block, which was observed
        // during discovery (reached via the fallthrough path). Speculative instructions are guarded
        // per-instruction, but the JMP into 'done' must be a plain goto: the observed convergence target
        // is already confirmed by execution and must never be re-verified.
        GeneratedCodeRunOptions options = new() {
            MaxCycles = 1000,
            EnableSpeculativeCfgExploration = true
        };
        GeneratedCodeMachineTestRunner runner = new();
        (_, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("speculative_branch", options);
        string source = generatedProgram.SourceText;

        // The speculative block entry at F000:0013 is guarded.
        source.Should().Contain("VerifySpeculativeEntryOrFail(cs1, 0x0013",
            "the speculative block entry at F000:0013 must be guarded");

        // The observed convergence target 'done' (F000:001A) must NOT be guarded: the speculative arm
        // reaches it via a plain goto, not a guarded entry. Per-instruction guards cover the speculative
        // instructions themselves (e.g. the JMP at F000:0018), but never the observed target they jump to.
        source.Should().NotContain("VerifySpeculativeEntryOrFail(cs1, 0x001A",
            "the observed 'done' block at F000:001A is a convergence target and must be reached by a plain goto, never a speculative guard");
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
    /// Direct call entry on speculative path. Discovery run (selector=0) never calls the
    /// subroutine. The speculative path explores into the callee but does NOT speculate the
    /// call continuation. When run with selector=1, the generated code enters the callee via
    /// the speculative guard but after ret hits "no continuation was observed" (expected).
    /// This validates that call entry IS explored but call continuation is NOT.
    /// </summary>
    [Fact]
    public void SpeculativeCallEntryExploresCalleeButNotContinuation() {
        GeneratedCodeRunOptions discoveryOptions = new() {
            MaxCycles = 1000,
            EnableSpeculativeCfgExploration = true
        };
        GeneratedCodeMachineTestRunner runner = new();
        (_, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("speculative_call_entry", discoveryOptions);
        string source = generatedProgram.SourceText;

        // The speculative call path must be guarded
        source.Should().Contain("VerifySpeculativeEntryOrFail",
            "the speculative call path must be guarded");

        // Call continuation is NOT speculated - it should produce FailAsUntested
        source.Should().Contain("but no continuation was observed during discovery",
            "call continuations are not speculated per design - they stay as untested");
    }

    /// <summary>
    /// Convergence onto observed code - both paths reach the same merge point.
    /// Discovery (selector=0) observes the mergepoint via path A. Speculative path B (selector=1)
    /// converges onto the same observed mergepoint block. No duplicate, just a goto.
    /// </summary>
    [Fact]
    public void SpeculativeConvergenceOntoObservedCodeExecutesCorrectly() {
        GeneratedCodeRunOptions discoveryOptions = new() {
            MaxCycles = 1000,
            EnableSpeculativeCfgExploration = true
        };
        GeneratedCodeMachineTestRunner runner = new();
        (_, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("speculative_convergence", discoveryOptions);
        string source = generatedProgram.SourceText;

        CompiledGeneratedOverride compiledOverride = new GeneratedOverrideCompiler().CompileSupplier(source);

        // Discovery path (selector=0): path A writes 0xAA, mergepoint writes 0xCC, 0xFF
        byte[] expectedDiscovery = new byte[0x404];
        expectedDiscovery[0x400] = 0x01;
        expectedDiscovery[0x401] = 0xAA;
        expectedDiscovery[0x402] = 0xCC;
        expectedDiscovery[0x403] = 0xFF;
        RunWithCompiledOverride("speculative_convergence", compiledOverride, expectedDiscovery, 1000);

        // Speculative path (selector=1): path B writes 0xBB, same mergepoint writes 0xCC, 0xFF
        byte[] expectedSpeculative = new byte[0x404];
        expectedSpeculative[0x400] = 0x01;
        expectedSpeculative[0x401] = 0xBB;
        expectedSpeculative[0x402] = 0xCC;
        expectedSpeculative[0x403] = 0xFF;
        RunWithCompiledOverride("speculative_convergence", compiledOverride, expectedSpeculative, 1000,
            machine => { machine.Memory.UInt8[0, 0x0500] = 0x01; });
    }

    /// <summary>
    /// Invalid opcode hard-stop. The generated source still has FailAsUntested for the arm
    /// that would decode into invalid opcodes (the explorer hard-stopped).
    /// </summary>
    [Fact]
    public void SpeculativeInvalidOpcodeStaysFailAsUntested() {
        GeneratedCodeRunOptions options = new() {
            MaxCycles = 1000,
            EnableSpeculativeCfgExploration = true
        };
        GeneratedCodeMachineTestRunner runner = new();
        (_, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("speculative_invalid_opcode", options);
        string source = generatedProgram.SourceText;

        source.Should().Contain("FailAsUntested",
            "an arm that decodes to invalid opcodes should remain untested (explorer hard-stopped)");

        // F000:0013 is the JNZ target that decodes into the data region (0xFF 0xFF invalid opcodes).
        // The explorer hard-stops there, so that arm must stay FailAsUntested and must NOT get a
        // speculative guard - a guard would imply the explorer successfully speculated the path.
        source.Should().NotContain("VerifySpeculativeEntryOrFail(cs1, 0x0013",
            "the invalid-opcode data region at F000:0013 must not be guarded; the explorer hard-stopped on it");
    }

    /// <summary>
    /// Mixed-block guard placement. The generated code for the speculative fallthrough arm
    /// must include a guard and execute correctly on both paths.
    /// </summary>
    [Fact]
    public void SpeculativeMixedBlockGuardPlacement() {
        GeneratedCodeRunOptions discoveryOptions = new() {
            MaxCycles = 1000,
            EnableSpeculativeCfgExploration = true
        };
        GeneratedCodeMachineTestRunner runner = new();
        (_, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("speculative_mixed_block", discoveryOptions);
        string source = generatedProgram.SourceText;

        source.Should().Contain("VerifySpeculativeEntryOrFail",
            "the unobserved fallthrough arm must be guarded");

        CompiledGeneratedOverride compiledOverride = new GeneratedOverrideCompiler().CompileSupplier(source);

        // Discovery path (selector=0): JE taken, writes 0xDD
        byte[] expectedDiscovery = new byte[0x403];
        expectedDiscovery[0x400] = 0x01;
        expectedDiscovery[0x401] = 0xDD;
        expectedDiscovery[0x402] = 0xAA;
        RunWithCompiledOverride("speculative_mixed_block", compiledOverride, expectedDiscovery, 1000);

        // Speculative path (selector=1): JE not taken, fallthrough writes 0xEE
        byte[] expectedSpeculative = new byte[0x403];
        expectedSpeculative[0x400] = 0x01;
        expectedSpeculative[0x401] = 0xEE;
        expectedSpeculative[0x402] = 0xAA;
        RunWithCompiledOverride("speculative_mixed_block", compiledOverride, expectedSpeculative, 1000,
            machine => { machine.Memory.UInt8[0, 0x0500] = 0x01; });
    }

    /// <summary>
    /// SMC inside speculative run triggers guard failure.
    /// The speculative_smc_guard fixture writes to its own code bytes on the speculative path.
    /// The generated source must have VerifySpeculativeEntryOrFail. When the speculative path
    /// is taken, the SMC invalidates the run signature and the guard fires.
    /// </summary>
    [Fact]
    public void SpeculativeSmcGuardSourceContainsGuard() {
        GeneratedCodeRunOptions options = new() {
            MaxCycles = 1000,
            EnableSpeculativeCfgExploration = true
        };
        GeneratedCodeMachineTestRunner runner = new();
        (_, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("speculative_smc_guard", options);
        string source = generatedProgram.SourceText;

        source.Should().Contain("VerifySpeculativeEntryOrFail",
            "the speculative SMC path must be guarded so that runtime SMC invalidates the run");
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
        GeneratedCodeRunOptions discoveryOptions = new() {
            MaxCycles = 1000,
            EnableSpeculativeCfgExploration = true
        };
        GeneratedCodeMachineTestRunner runner = new();
        (_, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("speculative_smc_guard", discoveryOptions);
        string source = generatedProgram.SourceText;

        source.Should().Contain("VerifySpeculativeEntryOrFail",
            "the speculative SMC path must be guarded");

        CompiledGeneratedOverride compiledOverride = new GeneratedOverrideCompiler().CompileSupplier(source);

        // Discovery path (selector=0): no SMC on this path, runs to completion.
        byte[] expectedDiscovery = new byte[0x403];
        expectedDiscovery[0x400] = 0x01;
        expectedDiscovery[0x401] = 0xDD;
        expectedDiscovery[0x402] = 0xAA;
        RunWithCompiledOverride("speculative_smc_guard", compiledOverride, expectedDiscovery, 1000);

        // Speculative path (selector=1): the first speculative instruction (F000:0013) rewrites the
        // ModRM byte of the second speculative instruction (F000:0019) in the same block. The guard
        // emitted before that second instruction detects the divergence and throws. The fired guard
        // is the one at F000:0019, not the block-entry guard at F000:0013, which proves the
        // detection happens mid-block rather than only at entry.
        Action act = () => RunWithCompiledOverride("speculative_smc_guard", compiledOverride, [], 1000,
            machine => { machine.Memory.UInt8[0, 0x0500] = 0x01; });

        act.Should().Throw<Spice86.Core.Emulator.Errors.InvalidVMOperationException>()
            .WithInnerException<Spice86.Shared.Emulator.Errors.UnrecoverableException>()
            .WithMessage("*Speculative code at F000:0019 no longer matches memory*");
    }

    /// <summary>
    /// Discard-on-divergence. Generated code with speculative path B is run after the memory
    /// at B's target has been modified. The VerifySpeculativeEntryOrFail guard fires (memory changed)
    /// and throws UnrecoverableException, preventing silent wrong execution.
    /// </summary>
    [Fact]
    public void SpeculativeDiscardOnDivergenceGuardFires() {
        GeneratedCodeRunOptions discoveryOptions = new() {
            MaxCycles = 1000,
            EnableSpeculativeCfgExploration = true
        };
        GeneratedCodeMachineTestRunner runner = new();
        (_, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("speculative_discard", discoveryOptions);
        string source = generatedProgram.SourceText;

        source.Should().Contain("VerifySpeculativeEntryOrFail",
            "the speculative arm must have a guard");

        CompiledGeneratedOverride compiledOverride = new GeneratedOverrideCompiler().CompileSupplier(source);

        // Discovery path (selector=0): works fine
        byte[] expectedDiscovery = new byte[0x403];
        expectedDiscovery[0x400] = 0x01;
        expectedDiscovery[0x401] = 0xDD;
        expectedDiscovery[0x402] = 0xAA;
        RunWithCompiledOverride("speculative_discard", compiledOverride, expectedDiscovery, 1000);

        // Speculative path (selector=1) with memory modification: guard fires and throws.
        // This proves the guard detects byte-level divergence and prevents silent wrong execution.
        Action act = () => RunWithCompiledOverride("speculative_discard", compiledOverride, [], 1000,
            machine => {
                machine.Memory.UInt8[0, 0x0500] = 0x01;
                // Modify the immediate byte at F000:0017 from 0xEE to 0xEF
                machine.Memory.UInt8[0xF000, 0x0017] = 0xEF;
            });

        act.Should().Throw<Spice86.Core.Emulator.Errors.InvalidVMOperationException>()
            .WithInnerException<Spice86.Shared.Emulator.Errors.UnrecoverableException>()
            .WithMessage("*Speculative code at F000:0013 no longer matches memory*");
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
    /// Generation validation: the mouse IRQ handler (vector 0x74, BiosMouseInt74Handler) never
    /// fires during headless discovery because there's no mouse activity. With speculation + seeding,
    /// it must still appear in the generated source as a DefineFunction at its F000 address and the
    /// generated code must compile. This is the exact crash scenario the feature was designed to fix.
    /// </summary>
    [Fact]
    public void MouseIrqHandlerEmittedInGeneratedCodeDespiteNeverFiring() {
        string comFileName = Path.GetFullPath("Resources/cpuTests/intchain.com");
        GeneratedCodeRunOptions options = new() {
            MaxCycles = 1000,
            InstallInterruptVectors = true,
            EnableSpeculativeCfgExploration = true
        };
        GeneratedCodeMachineTestRunner runner = new();
        (_, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource(comFileName, options);
        string source = generatedProgram.SourceText;

        // The mouse IRQ handler (vector 0x74) is at F000:005D in the standard layout.
        // It must have a DefineFunction even though no mouse event fired during discovery.
        // Look for a DefineFunction that references 0x005D (the mouse handler offset).
        source.Should().Contain("0x005D",
            "the mouse IRQ handler (BiosMouseInt74Handler, vector 0x74) must be emitted as a function "
            + "even though no mouse event fires during headless discovery - this is the exact crash scenario");

        // The generated source must contain the far call to the RETF stub. Now that far call imm
        // registers its target as a static successor, the callee IS explored speculatively and the
        // code generator emits a direct call (not SearchFunctionOverride). Verify the far call target
        // (the RETF stub) is present as a defined function in the generated source.
        source.Should().Contain("FarCall",
            "the mouse handler's far call to the RETF stub must be lowered as a FarCall "
            + "since far call imm now registers its callee as a static successor for speculative exploration");

        // Must compile.
        new GeneratedOverrideCompiler().CompileSupplier(source);
    }

    /// <summary>
    /// End-to-end: with DOS initialized and speculation on, the generated code for
    /// intchain.com (which exercises INT chains through F000 handlers) compiles AND runs
    /// successfully as an override. This proves seeded handlers reach generation and execute.
    /// </summary>
    [Fact]
    public void ExternalEventHandlerGeneratedOverrideCompilesAndRuns() {
        string comFileName = Path.GetFullPath("Resources/cpuTests/intchain.com");
        GeneratedCodeRunOptions options = new() {
            MaxCycles = 1000,
            InstallInterruptVectors = true,
            EnableSpeculativeCfgExploration = true
        };
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(comFileName, [], options);
    }

    /// <summary>
    /// The single speculation-off scenario: it proves why speculation is enabled everywhere else.
    ///
    /// Discovery observes only the fallthrough arm (selector=0). With speculation off the unobserved
    /// JNZ arm is never explored, so the generator emits FailAsUntested for it instead of a guarded
    /// speculative branch. The override therefore runs correctly on the observed path but, when run
    /// with selector=1 (the path speculative discovery would have explored), reaches the untested arm
    /// and throws. This is the missing-node crash that speculative discovery prevents.
    /// </summary>
    [Fact]
    public void SpeculationOffLeavesUnobservedArmUntestedAndCrashesOnThatPath() {
        GeneratedCodeRunOptions options = new() {
            MaxCycles = 1000,
            EnableSpeculativeCfgExploration = false
        };
        GeneratedCodeMachineTestRunner runner = new();
        (_, GeneratedCSharpProgram generatedProgram) = runner.GenerateProgramAndSource("speculative_branch", options);
        string source = generatedProgram.SourceText;

        source.Should().Contain("FailAsUntested",
            "with speculation off the unobserved JNZ arm must be left untested");
        source.Should().NotContain("VerifySpeculativeEntryOrFail",
            "with speculation off no speculative guard is emitted");

        CompiledGeneratedOverride compiledOverride = new GeneratedOverrideCompiler().CompileSupplier(source);

        // Observed path (selector=0): runs to the correct result.
        byte[] expectedDiscovery = new byte[0x403];
        expectedDiscovery[0x400] = 0x01;
        expectedDiscovery[0x401] = 0xDD;
        expectedDiscovery[0x402] = 0xAA;
        RunWithCompiledOverride("speculative_branch", compiledOverride, expectedDiscovery, 1000);

        // Unobserved path (selector=1): the untested arm is reached and the generated code throws,
        // proving the speculative node omitted by discovery is actually needed at runtime.
        Action act = () => RunWithCompiledOverride("speculative_branch", compiledOverride, [], 1000,
            machine => { machine.Memory.UInt8[0, 0x0500] = 0x01; });

        act.Should().Throw<Spice86.Core.Emulator.Errors.InvalidVMOperationException>()
            .WithInnerException<Spice86.Shared.Emulator.Errors.UnrecoverableException>()
            .WithMessage("*Untested code reached*");
    }

    /// <summary>
    /// Interpreter-side selector=1 coverage. The golden MachineTest fixtures only run the observed
    /// (selector=0) path; the selector=1 arm of each fixture is otherwise exercised only through a
    /// generated override. These tests run the same bins in the pure interpreter (no override) with
    /// selector=1 selected and assert the resulting memory dump, validating that the interpreter
    /// itself executes the speculative arm to the correct result.
    /// </summary>
    [Fact]
    public void InterpreterExecutesSpeculativeBranchSelectorOnePath() {
        byte[] expected = new byte[0x403];
        expected[0x400] = 0x01;
        expected[0x401] = 0xEE;
        expected[0x402] = 0xAA;
        RunInterpreter("speculative_branch", expected, 1000,
            machine => { machine.Memory.UInt8[0, 0x0500] = 0x01; });
    }

    [Fact]
    public void InterpreterExecutesSpeculativeClosureSelectorOnePath() {
        byte[] expected = new byte[0x403];
        expected[0x400] = 0x01;
        expected[0x401] = 0x03;
        expected[0x402] = 0xBB;
        RunInterpreter("speculative_closure", expected, 1000,
            machine => { machine.Memory.UInt8[0, 0x0500] = 0x01; });
    }

    [Fact]
    public void InterpreterExecutesSpeculativeConvergenceSelectorOnePath() {
        byte[] expected = new byte[0x404];
        expected[0x400] = 0x01;
        expected[0x401] = 0xBB;
        expected[0x402] = 0xCC;
        expected[0x403] = 0xFF;
        RunInterpreter("speculative_convergence", expected, 1000,
            machine => { machine.Memory.UInt8[0, 0x0500] = 0x01; });
    }

    [Fact]
    public void InterpreterExecutesSpeculativeMixedBlockSelectorOnePath() {
        byte[] expected = new byte[0x403];
        expected[0x400] = 0x01;
        expected[0x401] = 0xEE;
        expected[0x402] = 0xAA;
        RunInterpreter("speculative_mixed_block", expected, 1000,
            machine => { machine.Memory.UInt8[0, 0x0500] = 0x01; });
    }

    /// <summary>
    /// Runs the binary in the pure interpreter (no generated override), applying an optional machine
    /// configuration before execution, then asserts the memory dump matches expected.
    /// </summary>
    private static void RunInterpreter(string binName, byte[] expected, long maxCycles,
        Action<Machine> configureMachine) {
        using Spice86Creator creator = new(binName: binName, maxCycles: maxCycles,
            jitMode: JitMode.InterpretedOnly);
        using Spice86DependencyInjection di = creator.Create();
        configureMachine(di.Machine);
        di.ProgramExecutor.Run();

        byte[] actual = di.Machine.Memory.ReadRam((uint)expected.Length);
        actual.Should().Equal(expected);
    }

    /// <summary>
    /// Runs the binary with a pre-compiled override supplier and asserts memory matches expected.
    /// This allows testing a generated override compiled from one discovery run against different inputs.
    /// </summary>
    private static void RunWithCompiledOverride(string binName, CompiledGeneratedOverride compiledOverride,
        byte[] expected, long maxCycles, Action<Machine>? configureMachine = null) {
        using Spice86Creator creator = new(binName: binName, maxCycles: maxCycles,
            jitMode: JitMode.InterpretedOnly, overrideSupplier: compiledOverride.Supplier);
        using Spice86DependencyInjection di = creator.Create();
        configureMachine?.Invoke(di.Machine);
        di.FunctionCatalogue.FunctionInformations.Values
            .Should().Contain(fi => fi.HasOverride);
        di.ProgramExecutor.Run();

        byte[] actual = di.Machine.Memory.ReadRam((uint)expected.Length);
        actual.Should().Equal(expected);
    }
}
