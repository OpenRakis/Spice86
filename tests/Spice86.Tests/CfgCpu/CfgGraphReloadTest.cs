namespace Spice86.Tests.CfgCpu;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.ReverseEngineer.Graph;
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Core.Emulator.StateSerialization.CfgReload;
using Spice86.Core.Emulator.VM;
using Spice86.Logging;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Tests.Utility;

using System.Text.Json;
using System.Text.Json.Nodes;

using Xunit;

/// <summary>
/// Tests for CFG graph persistence: run a bin, capture the reload artifact, reload it into a fresh
/// machine, and assert the reconstructed graph matches the original.
///
/// Reloaded instructions start non-live by design (Option 1): liveness, current-cache membership and
/// memory-write breakpoints are coupled and are established together only by
/// <c>CurrentInstructions.SetAsCurrent</c> when execution first reaches a node. These tests therefore
/// verify structure at t=0 (ignoring liveness) and verify liveness/breakpoint behaviour only after
/// the feeder/execution path runs.
/// </summary>
public class CfgGraphReloadTest {
    /// <summary>
    /// Bins used by the precise (x1000 id, resume, breakpoint, dump-level) round-trip tests. Includes
    /// <c>lockprefix</c> so the invalid-instruction reconstruction path (which consumes more than one
    /// id per parse, see <c>CfgNodeReconstructor.ParsePreservingDumpedId</c>) is directly exercised.
    /// </summary>
    public static IEnumerable<object[]> RoundTripBins => [
        ["segpr"],
        ["divfaultloop"],
        ["selfmodifyvalue"],
        ["selfmodifycall"],
        ["selfmodifyterminator"],
        ["lockprefix"],
    ];

    /// <summary>
    /// Every bin that has a deterministic, golden-validated CFG graph (i.e. a fixture under
    /// res/DumpedCfgBlocks). Running the round-trip over all of them ensures reload does not break on
    /// weird-but-tested cases (faults, interrupts, selectors, prefixes, partitioning, 386 features).
    /// <c>intchain.com</c> is excluded: it is a DOS COM file with no deterministic graph fixture.
    /// </summary>
    public static IEnumerable<object[]> AllReloadBins => new[] {
        "add", "bcdcnv", "bitwise", "cmpneg", "control", "datatrnf", "div", "div2", "divfaultloop",
        "externalint", "interrupt", "jmpmov", "jump1", "jump2", "linearsamesegmenteddifferent",
        "lockprefix", "mul", "partition_cross_function_loop", "partition_indirect_call_jump",
        "partition_jump_into_function_middle", "partition_mixed_activation_cycle",
        "partition_multi_entry_dominated_shared", "partition_multi_entry_irreducible_shared",
        "partition_shared_tail", "rep", "returnedterminator", "rotate", "segpr", "selfmodifycall",
        "selfmodifyinstructions", "selfmodifyje", "selfmodifyterminator", "selfmodifyvalue", "shifts",
        "sticli", "strings", "sub", "test386",
    }.Select(name => new object[] { name });

    /// <summary>
    /// The expensive end-to-end run-vs-run check (two full executions per bin) is scoped to a
    /// representative subset rather than the full matrix: one fault bin, one selector/self-modifying
    /// bin of each shape, one prefix bin, one partition bin and one 386 bin. The cheaper no-execute
    /// structural round-trip (<see cref="ReloadedGraphMatchesOriginal"/>) still runs over every bin.
    /// </summary>
    public static IEnumerable<object[]> EndToEndBins => new[] {
        "divfaultloop", "selfmodifyvalue", "selfmodifycall", "selfmodifyterminator", "selfmodifyje",
        "lockprefix", "partition_indirect_call_jump", "test386",
    }.Select(name => new object[] { name });

    /// <summary>
    /// Per-bin run configuration. Mirrors the knobs the corresponding MachineTest cases use so each bin
    /// reaches the same deterministic end state; bins not listed run with the defaults.
    /// </summary>
    private static (long MaxCycles, bool EnablePit, bool EnableA20Gate) GetBinConfig(string binName) => binName switch {
        "externalint" => (0xFFFFFFF, true, false),
        "linearsamesegmenteddifferent" => (100000L, false, true),
        "test386" => (long.MaxValue, false, false),
        _ => (100000L, false, false),
    };

    [Theory]
    [MemberData(nameof(AllReloadBins))]
    public void ReloadedGraphMatchesOriginal(string binName) {
        // Run the bin to completion and capture the reload artifact + reference graph.
        (CfgReloadDump dump, _) = CaptureDumpAndMemory(binName);
        string referenceJson = RunAndSerializeGraph(binName);

        // Reload into a fresh machine (no execution) and export.
        string reloadedJson;
        using (LoggerService loggerService = new())
        using (Spice86Creator creator = CreateCreator(binName))
        using (Spice86DependencyInjection di = creator.Create()) {
            using CfgNodeExecutionCompiler compiler = NewCompiler(loggerService);
            CfgGraphReloader reloader = new(di.Machine.CfgCpu, di.Machine.CpuState, compiler, di.CfgIdAllocator);
            reloader.Reload(dump);
            reloadedJson = CfgBlocksTestJson.Serialize(di.Machine.CfgCpu.ExecutionContextManager);
        }

        // Compare structure (edges, blocks, pred/succ, sigHex disassembly) but ignore the `dead`
        // liveness flag: reloaded nodes are intentionally non-live until execution promotes them, so
        // every block reads dead at t=0. Block array order also follows BFS seeding from the
        // non-restored last-executed block, so it is normalized away.
        AssertStructurallyEqual(referenceJson, reloadedJson, ignoreDead: true);
    }

    [Theory]
    [MemberData(nameof(RoundTripBins))]
    public void ReloadedGraphReExportsToSameDump(string binName) {
        // Direct dump-level round-trip: reload the captured dump into a fresh machine, re-export, and
        // assert the re-exported dump equals the original. Unlike ReloadedGraphMatchesOriginal (which
        // compares the lossy block-graph projection), this asserts exactly what the reconstructor must
        // restore: typed instruction edges, per-node MaxSucc, selector dispatch edges and ids.
        CfgReloadDump original = CaptureDump(binName);

        using LoggerService loggerService = new();
        using Spice86Creator creator = CreateCreator(binName);
        using Spice86DependencyInjection di = creator.Create();
        using CfgNodeExecutionCompiler compiler = NewCompiler(loggerService);
        CfgGraphReloader reloader = new(di.Machine.CfgCpu, di.Machine.CpuState, compiler, di.CfgIdAllocator);
        reloader.Reload(original);

        CfgReloadDump reExported = new CfgReloadExporter().Export(di.Machine.CfgCpu.ExecutionContextManager);
        // Records compare structurally, but the array members are reference-typed, so compare the
        // canonical (sorted) JSON the exporter already produces deterministically.
        Assert.Equal(SerializeDump(original), SerializeDump(reExported));
    }

    [Theory]
    [MemberData(nameof(RoundTripBins))]
    public void ReloadPreservesIdsAndSeedsAllocator(string binName) {
        // The x1000 trick: rewrite every node/block id x as x*1000 (keeping all cross-references
        // consistent) and assert reconstruction honoured the dumped ids verbatim and seeded the
        // allocator to max(reloaded ids) + 1.
        CfgReloadDump dump = CaptureDump(binName);
        CfgReloadDump scaledDump = ScaleIds(dump, 1000);
        HashSet<int> expectedIds = new(scaledDump.Nodes.Select(n => n.Id).Concat(scaledDump.Blocks.Select(b => b.Id)));
        int maxId = expectedIds.Count == 0 ? -1 : expectedIds.Max();

        using LoggerService loggerService = new();
        using Spice86Creator creator = CreateCreator(binName);
        using Spice86DependencyInjection di = creator.Create();
        using CfgNodeExecutionCompiler compiler = NewCompiler(loggerService);
        CfgGraphReloader reloader = new(di.Machine.CfgCpu, di.Machine.CpuState, compiler, di.CfgIdAllocator);
        reloader.Reload(scaledDump);

        // Every reconstructed node/block id must be exactly the x1000 set.
        HashSet<int> actualIds = CollectAllNodeAndBlockIds(di.Machine.CfgCpu.ExecutionContextManager);
        Assert.Equal(expectedIds.OrderBy(i => i), actualIds.OrderBy(i => i));

        // The next id the live allocator hands out must be max(reloaded ids) + 1.
        int nextAllocated = di.CfgIdAllocator.AllocateId();
        Assert.Equal(maxId + 1, nextAllocated);
    }

    [Theory]
    [MemberData(nameof(RoundTripBins))]
    public void NewDiscoveryAfterReloadGetsNonCollidingId(string binName) {
        // The real cross-run concern: after reload, when the live CPU discovers a brand-new node
        // (territory not in the dump), the live allocator must not hand out an id that collides with a
        // reloaded one. Reload seeds the live allocator to max(reloaded id) + 1; reconstruction uses a
        // separate private allocator, so the live allocator is untouched until first live discovery.
        // Use the x1000 artifact so every reloaded id is huge and a colliding fresh id would be obvious.
        (CfgReloadDump dump, byte[] memoryImage) = CaptureDumpAndMemory(binName);
        CfgReloadDump scaledDump = ScaleIds(dump, 1000);
        HashSet<int> reloadedIds = new(scaledDump.Nodes.Select(n => n.Id).Concat(scaledDump.Blocks.Select(b => b.Id)));
        int maxReloadedId = reloadedIds.Max();

        using LoggerService loggerService = new();
        using Spice86Creator creator = CreateCreator(binName);
        using Spice86DependencyInjection di = creator.Create();
        Machine machine = di.Machine;
        RestoreInstructionBytes(machine.Memory, memoryImage, scaledDump);
        using CfgNodeExecutionCompiler compiler = NewCompiler(loggerService);
        CfgGraphReloader reloader = new(machine.CfgCpu, machine.CpuState, compiler, di.CfgIdAllocator);
        reloader.Reload(scaledDump);

        // Pick a scratch address with no reloaded node, plant a NOP there, and have the live feeder
        // discover it fresh (this parses through the same allocator the reloader just seeded).
        SegmentedAddress scratchAddress = FindUnusedAddress(scaledDump);
        machine.Memory.UInt8[scratchAddress.Linear] = 0x90; // NOP
        InstructionsFeeder feeder = machine.CfgCpu.CfgNodeFeeder.InstructionsFeeder;
        CfgInstruction discovered = feeder.GetInstructionFromMemory(scratchAddress);

        // It is genuinely new (not a reloaded node) and its id is past the whole reloaded id range.
        Assert.Equal(scratchAddress, discovered.Address);
        Assert.DoesNotContain(discovered.Id, reloadedIds);
        Assert.True(discovered.Id > maxReloadedId,
            $"newly discovered node id {discovered.Id} must be past the reloaded range (max {maxReloadedId})");
    }

    /// <summary>
    /// Returns a segmented address whose offset is not occupied by any reloaded instruction node, so a
    /// freshly planted instruction there is discovered (not matched against a reloaded node).
    /// </summary>
    private static SegmentedAddress FindUnusedAddress(CfgReloadDump dump) {
        HashSet<SegmentedAddress> usedAddresses = new(dump.Nodes.Select(n => ParseAddress(n.Addr)));
        // Same segment as the program (0x170) keeps the address in plain conventional RAM; walk offsets
        // until one is free. Bins are tiny, so a free slot is found almost immediately.
        for (ushort offset = 0x1000; offset < 0x2000; offset++) {
            SegmentedAddress candidate = new(0x170, offset);
            if (!usedAddresses.Contains(candidate)) {
                return candidate;
            }
        }
        throw new InvalidOperationException("No unused scratch address found for the discovery test");
    }

    [Theory]
    [MemberData(nameof(RoundTripBins))]
    public void ResumeAfterReloadReconnectsAndPromotesToLive(string binName) {
        // Prove resumed execution reuses the reloaded PreviousInstructions node (facts 2 and 3) and
        // that promotion goes through SetAsCurrent, which makes the node live AND current together.
        // Use the x1000 artifact so the reused node's id is unmistakably the reloaded one.
        (CfgReloadDump dump, byte[] memoryImage) = CaptureDumpAndMemory(binName);
        CfgReloadDump scaledDump = ScaleIds(dump, 1000);
        SegmentedAddress entryAddress = ParseAddress(scaledDump.EntryPoints[0]);
        int expectedReloadedId = scaledDump.Nodes
            .Where(n => n.Type == CfgReloadNodeType.Instruction && ParseAddress(n.Addr) == entryAddress)
            .Select(n => n.Id)
            .Single();

        using LoggerService loggerService = new();
        using Spice86Creator creator = CreateCreator(binName);
        using Spice86DependencyInjection di = creator.Create();
        Machine machine = di.Machine;
        // Put the dump-time bytes in place so the entry node matches memory and is reused on resume.
        RestoreInstructionBytes(machine.Memory, memoryImage, scaledDump);
        using CfgNodeExecutionCompiler compiler = NewCompiler(loggerService);
        CfgGraphReloader reloader = new(machine.CfgCpu, machine.CpuState, compiler, di.CfgIdAllocator);
        reloader.Reload(scaledDump);

        InstructionsFeeder feeder = machine.CfgCpu.CfgNodeFeeder.InstructionsFeeder;
        // Before resume: reloaded node exists but is non-live and not in the current cache.
        Assert.Null(feeder.CurrentInstructions.GetAtAddress(entryAddress));

        // Drive the exact feeder path resumed execution uses.
        CfgInstruction resumed = feeder.GetInstructionFromMemory(entryAddress);

        // It must be the reloaded instance (reused, not re-parsed) and now live + current.
        Assert.Equal(expectedReloadedId, resumed.Id);
        Assert.True(resumed.IsLive);
        CfgInstruction? current = feeder.CurrentInstructions.GetAtAddress(entryAddress);
        Assert.NotNull(current);
        Assert.Same(resumed, current);
    }

    [Theory]
    [MemberData(nameof(RoundTripBins))]
    public void PromotedReloadedNodeHasMemoryWriteBreakpoint(string binName) {
        // The invariant under test: a reloaded node, once live, has its memory-write breakpoints
        // installed. We prove it behaviourally: after promotion, writing a different byte over the
        // instruction must evict it (clear from the current cache and flip it non-live). If no
        // breakpoint had been installed, the write would go unnoticed and the node would stay live.
        (CfgReloadDump dump, byte[] memoryImage) = CaptureDumpAndMemory(binName);
        SegmentedAddress entryAddress = ParseAddress(dump.EntryPoints[0]);

        using LoggerService loggerService = new();
        using Spice86Creator creator = CreateCreator(binName);
        using Spice86DependencyInjection di = creator.Create();
        Machine machine = di.Machine;
        RestoreInstructionBytes(machine.Memory, memoryImage, dump);
        using CfgNodeExecutionCompiler compiler = NewCompiler(loggerService);
        CfgGraphReloader reloader = new(machine.CfgCpu, machine.CpuState, compiler, di.CfgIdAllocator);
        reloader.Reload(dump);

        InstructionsFeeder feeder = machine.CfgCpu.CfgNodeFeeder.InstructionsFeeder;
        CfgInstruction resumed = feeder.GetInstructionFromMemory(entryAddress);
        Assert.True(resumed.IsLive);
        Assert.Same(resumed, feeder.CurrentInstructions.GetAtAddress(entryAddress));

        // Overwrite the opcode byte (a non-null signature byte, hence breakpointed) with a different
        // value. This goes through the memory write path and must trip the instruction's breakpoint.
        uint opcodeAddress = entryAddress.Linear;
        byte originalByte = machine.Memory.UInt8[opcodeAddress];
        machine.Memory.UInt8[opcodeAddress] = (byte)(originalByte ^ 0xFF);

        // Breakpoint fired => node evicted: no longer current and no longer live.
        Assert.Null(feeder.CurrentInstructions.GetAtAddress(entryAddress));
        Assert.False(resumed.IsLive);
    }

    [Theory]
    [MemberData(nameof(EndToEndBins))]
    public void EndToEndReloadThenRunMatchesCleanRun(string binName) {
        // Faithful full-feature check through the real DI path: a clean run vs. a run that first
        // reloads the dumped graph (ReloadCfgGraph=true) and then executes from the reset vector.
        // Both must end with identical memory AND an identical CFG graph (including liveness), proving
        // reload neither corrupts execution nor breaks self-modifying-code detection: reloaded nodes
        // are promoted (live + breakpoint) as execution reaches them, exactly like a fresh discovery.
        using TempFile recordedDataDirectory = new(nameof(EndToEndReloadThenRunMatchesCleanRun));

        // Clean run: produces the dump (spice86dumpCfgReload.json) and the reference state.
        byte[] referenceMemory;
        string referenceGraph;
        using (Spice86Creator creator = CreateCreator(binName, recordedDataDirectory.Path, reloadCfgGraph: false))
        using (Spice86DependencyInjection di = creator.Create()) {
            di.ProgramExecutor.Run();
            referenceMemory = di.Machine.Memory.ReadRam();
            referenceGraph = CfgBlocksTestJson.Serialize(di.Machine.CfgCpu.ExecutionContextManager);
        }

        // Reload-then-run: reloads the dumped graph at startup, then runs to completion.
        byte[] reloadedMemory;
        string reloadedGraph;
        using (Spice86Creator creator = CreateCreator(binName, recordedDataDirectory.Path, reloadCfgGraph: true))
        using (Spice86DependencyInjection di = creator.Create()) {
            di.ProgramExecutor.Run();
            reloadedMemory = di.Machine.Memory.ReadRam();
            reloadedGraph = CfgBlocksTestJson.Serialize(di.Machine.CfgCpu.ExecutionContextManager);
        }

        Assert.Equal(referenceMemory, reloadedMemory);
        // Full equality including `dead`: both ran to completion, so liveness must converge too.
        AssertStructurallyEqual(referenceGraph, reloadedGraph);
    }

    private static Spice86Creator CreateCreator(string binName) {
        (long maxCycles, bool enablePit, bool enableA20Gate) = GetBinConfig(binName);
        return new Spice86Creator(binName: binName, maxCycles: maxCycles, enablePit: enablePit,
            enableA20Gate: enableA20Gate, jitMode: JitMode.InterpretedOnly);
    }

    private static Spice86Creator CreateCreator(string binName, string recordedDataDirectory, bool reloadCfgGraph) {
        (long maxCycles, bool enablePit, bool enableA20Gate) = GetBinConfig(binName);
        return new Spice86Creator(binName: binName, maxCycles: maxCycles, enablePit: enablePit,
            enableA20Gate: enableA20Gate, jitMode: JitMode.InterpretedOnly,
            recordedDataDirectory: recordedDataDirectory, reloadCfgGraph: reloadCfgGraph);
    }

    private static string RunAndSerializeGraph(string binName) {
        using Spice86Creator creator = CreateCreator(binName);
        using Spice86DependencyInjection di = creator.Create();
        di.ProgramExecutor.Run();
        return CfgBlocksTestJson.Serialize(di.Machine.CfgCpu.ExecutionContextManager);
    }

    private static CfgReloadDump CaptureDump(string binName) => CaptureDumpAndMemory(binName).Dump;

    private static (CfgReloadDump Dump, byte[] Memory) CaptureDumpAndMemory(string binName) {
        using Spice86Creator creator = CreateCreator(binName);
        using Spice86DependencyInjection di = creator.Create();
        di.ProgramExecutor.Run();
        Machine machine = di.Machine;
        CfgReloadDump dump = new CfgReloadExporter().Export(machine.CfgCpu.ExecutionContextManager);
        return (dump, machine.Memory.ReadRam());
    }

    private static SegmentedAddress ParseAddress(string addr) {
        Assert.True(SegmentedAddress.TryParse(addr, out SegmentedAddress? parsed));
        return parsed.GetValueOrDefault();
    }

    /// <summary>
    /// Writes the dump-time bytes covering each instruction node into memory, so the feeder's
    /// memory-matching reuses the reloaded node on resume. Only instruction byte ranges are written
    /// (not the whole RAM image) to avoid writing into read-only mapped regions such as the VGA ROM.
    /// </summary>
    private static void RestoreInstructionBytes(IMemory memory, byte[] memoryImage, CfgReloadDump dump) {
        foreach (CfgReloadNodeInfo node in dump.Nodes) {
            if (node.Bytes is null) {
                continue;
            }
            if (!SegmentedAddress.TryParse(node.Addr, out SegmentedAddress? parsed)) {
                continue;
            }
            uint linear = parsed.GetValueOrDefault().Linear;
            int length = node.Bytes.Length / 2;
            for (int i = 0; i < length && linear + i < memoryImage.Length; i++) {
                memory.UInt8[(uint)(linear + i)] = memoryImage[linear + i];
            }
        }
    }

    private static CfgNodeExecutionCompiler NewCompiler(ILoggerService loggerService) {
        return new CfgNodeExecutionCompiler(new CfgNodeExecutionCompilerMonitor(loggerService), loggerService, JitMode.InterpretedOnly);
    }

    private static string SerializeDump(CfgReloadDump dump) {
        return JsonSerializer.Serialize(dump, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Compares two CFG block graph JSON documents structurally, ignoring intentionally non-restored
    /// runtime fields and array orderings that depend on the (non-restored) last-executed block.
    /// </summary>
    private static void AssertStructurallyEqual(string referenceJson, string reloadedJson, bool ignoreDead = false) {
        string expected = Normalize(referenceJson, ignoreDead);
        string actual = Normalize(reloadedJson, ignoreDead);
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Removes the non-restored runtime fields and canonicalizes order-sensitive arrays so two graphs
    /// that differ only by BFS-seeding order compare equal. When <paramref name="ignoreDead"/> is set,
    /// also drops the memory-derived <c>dead</c> liveness flag from each block.
    /// </summary>
    private static string Normalize(string json, bool ignoreDead) {
        JsonObject? root = (JsonObject?)JsonNode.Parse(json);
        if (root is null) {
            return json;
        }
        root.Remove("currentContextDepth");
        root.Remove("currentContextEntryPoint");
        root.Remove("lastExecutedAddress");
        root.Remove("lastExecutedBlockId");

        // entryPointAddresses ordering follows discovery order, which the dump does not preserve, so
        // sort it for a stable comparison.
        if (root["entryPointAddresses"] is JsonArray entryPoints) {
            root["entryPointAddresses"] = SortStringArray(entryPoints);
        }

        // Sort blocks by id so BFS array order does not matter.
        if (root["blocks"] is JsonArray blocks) {
            if (ignoreDead) {
                foreach (JsonNode? block in blocks) {
                    (block as JsonObject)?.Remove("dead");
                }
            }
            root["blocks"] = SortArrayByInt(blocks, "id");
        }
        // Partitions are derived; their block lists are sets, so sort for stable comparison.
        if (root["partitions"] is JsonArray partitions) {
            foreach (JsonNode? partition in partitions) {
                if (partition is JsonObject partitionObject && partitionObject["blocks"] is JsonArray partitionBlocks) {
                    partitionObject["blocks"] = SortIntArray(partitionBlocks);
                }
            }
            root["partitions"] = SortArrayByInt(partitions, "id");
        }
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonArray SortArrayByInt(JsonArray array, string key) {
        List<JsonNode?> ordered = array.OrderBy(node => node?[key]?.GetValue<int>() ?? 0).ToList();
        JsonArray result = new();
        foreach (JsonNode? node in ordered) {
            result.Add(node?.DeepClone());
        }
        return result;
    }

    private static JsonArray SortIntArray(JsonArray array) {
        List<int> values = array.Select(node => node?.GetValue<int>() ?? 0).OrderBy(value => value).ToList();
        JsonArray result = new();
        foreach (int value in values) {
            result.Add(value);
        }
        return result;
    }

    private static JsonArray SortStringArray(JsonArray array) {
        List<string> values = array.Select(node => node?.GetValue<string>() ?? string.Empty)
            .OrderBy(value => value, StringComparer.Ordinal).ToList();
        JsonArray result = new();
        foreach (string value in values) {
            result.Add(value);
        }
        return result;
    }

    private static HashSet<int> CollectAllNodeAndBlockIds(ExecutionContextManager contextManager) {
        // Reuse the production traversal helper so the test's reachability definition matches the
        // exporter's (Successors + Predecessors) by construction.
        IEnumerable<ICfgNode> seeds = contextManager.ExecutionContextEntryPoints.Values
            .SelectMany(entrySet => entrySet);
        HashSet<int> ids = new();
        foreach (ICfgNode node in DepthFirstSearch.Enumerate(seeds, n => n.Successors.Concat(n.Predecessors))) {
            ids.Add(node.Id);
            if (node.ContainingBlock is CfgBlock block) {
                ids.Add(block.Id);
            }
        }
        return ids;
    }

    private static CfgReloadDump ScaleIds(CfgReloadDump dump, int factor) {
        CfgReloadNodeInfo[] nodes = dump.Nodes.Select(n => n with { Id = n.Id * factor }).ToArray();
        CfgReloadBlockInfo[] blocks = dump.Blocks.Select(b => b with {
            Id = b.Id * factor,
            Nodes = b.Nodes.Select(id => id * factor).ToArray()
        }).ToArray();
        // IdAllocatorNext is max(id) + 1, not an id, so it must be recomputed from the scaled ids
        // rather than multiplied (which would yield max*factor + factor instead of max*factor + 1).
        int maxScaledId = nodes.Select(n => n.Id).Concat(blocks.Select(b => b.Id)).DefaultIfEmpty(-1).Max();
        return dump with {
            IdAllocatorNext = maxScaledId + 1,
            Nodes = nodes,
            Edges = dump.Edges.Select(e => e with { From = e.From * factor, To = e.To * factor }).ToArray(),
            Blocks = blocks
        };
    }
}
