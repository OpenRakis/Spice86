namespace Spice86.Tests.Utility;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Shared.Interfaces;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Shared building/serialization of the CFG block graph for tests, so the graph comparison logic and
/// its JSON options live in one place instead of being duplicated across test classes.
/// </summary>
internal static class CfgBlocksTestJson {
    /// <summary>Serializer options matching the on-disk DumpedCfgBlocks fixtures.</summary>
    public static readonly JsonSerializerOptions Options = new() {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Builds the block graph model from a live execution context.</summary>
    public static CfgCpuGraph BuildGraph(ExecutionContextManager contextManager) {
        CfgBlocksJsonExporter exporter = CreateExporter();
        return exporter.BuildGraph(contextManager, null);
    }

    /// <summary>
    /// Serializes the combined graph (blocks + partition overlay) for a live execution context. Used by the
    /// reload round-trip tests that only need a stable, complete projection to compare two graphs.
    /// </summary>
    public static string Serialize(ExecutionContextManager contextManager) {
        return JsonSerializer.Serialize(BuildGraph(contextManager), Options);
    }

    /// <summary>
    /// Serializes the blocks-only on-disk view (the <c>CfgBlocks.json</c> dump) for a live execution context.
    /// </summary>
    public static string SerializeBlocks(ExecutionContextManager contextManager) {
        CfgBlocksJsonExporter exporter = CreateExporter();
        CfgCpuSnapshot snapshot = BuildSnapshot(contextManager);
        return JsonSerializer.Serialize(exporter.ToBlocksDump(snapshot), Options);
    }

    /// <summary>
    /// Serializes the partition overlay on-disk view (the <c>CfgPartitions.json</c> dump) for a live
    /// execution context.
    /// </summary>
    public static string SerializePartitions(ExecutionContextManager contextManager) {
        CfgBlocksJsonExporter exporter = CreateExporter();
        CfgCpuSnapshot snapshot = BuildSnapshot(contextManager);
        return JsonSerializer.Serialize(exporter.ToPartitionsDump(snapshot), Options);
    }

    private static CfgCpuSnapshot BuildSnapshot(ExecutionContextManager contextManager) {
        return CreateSnapshotBuilder().Build(contextManager, null);
    }

    private static CfgBlocksJsonExporter CreateExporter() {
        return new CfgBlocksJsonExporter(CreateSnapshotBuilder());
    }

    private static CfgCpuSnapshotBuilder CreateSnapshotBuilder() {
        State state = new(CpuModel.INTEL_80286);
        Memory memory = new(new AddressReadWriteBreakpoints(), new Ram(0x200000), new A20Gate(),
            new RealModeMmu386(), false);
        LoadedImageRegistry loadedImageRegistry = new();
        return new CfgCpuSnapshotBuilder(new CfgBlockGraphExporter(), new CfgFunctionPartitioner(),
            new FunctionCatalogue(),
            new GeneratorInputsCollector(new InstalledInterruptHandlers([]),
                new InterruptVectorTable(memory), new CallbackHandler(state, NullLogger.Instance),
                loadedImageRegistry, new SharedMouseData(),
                new ChangedMemoryMap(memory, new AddressReadWriteBreakpoints(), loadedImageRegistry),
                memory, new IOPortDispatcher(new(), state, NullLogger.Instance, false),
                Spice86Creator.ProgramEntryPointSegment),
            NullLogger.Instance);
    }
}
