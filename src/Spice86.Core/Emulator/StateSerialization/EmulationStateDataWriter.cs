namespace Spice86.Core.Emulator.StateSerialization;

using Microsoft.Extensions.Logging;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph;
using Spice86.Core.Emulator.StateSerialization.CfgReload;
using Spice86.Shared.Emulator.VM.Breakpoint.Serializable;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

/// <summary>
/// A class that provides functionality for writing various recorded data to files.
/// </summary>
public class EmulationStateDataWriter : EmulationStateDataIoHandler {
    private readonly State _state;
    private readonly ExecutionAddressesExtractor _executionAddressesExtractor;
    private readonly MemoryDataExporter _memoryDataExporter;
    private readonly ListingExporter _listingExporter;
    private readonly CfgCpuSnapshotBuilder _cfgCpuSnapshotBuilder;
    private readonly CfgBlocksJsonExporter _cfgBlocksJsonExporter;
    private readonly CfgCSharpDumper _cfgCSharpDumper;
    private readonly ExecutionContextManager _executionContextManager;
    private readonly FunctionCatalogue _functionCatalogue;
    private readonly ISerializableBreakpointsSource _serializableBreakpointsSource;
    private readonly Configuration _configuration;
    private readonly CfgNodeIndex _nodeIndex;
    private readonly DumpProgressReporter _progress;
    private readonly Lock _writeLock = new();

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="executionAddressesExtractor">The class that dumps machine code execution flow.</param>
    /// <param name="memoryDataExporter">The class used to dump main memory data properly.</param>
    /// <param name="listingExporter">The class used to dump asm listing of encountered instructions.</param>
    /// <param name="cfgCpuSnapshotBuilder">Builds the single CFG snapshot (export + partition) shared by the JSON and C# dumps.</param>
    /// <param name="cfgBlocksJsonExporter">The class used to dump the CFG block graph as JSON.</param>
    /// <param name="cfgCSharpDumper">The class used to dump the generated C# overrides.</param>
    /// <param name="executionContextManager">The execution context manager for CFG graph export.</param>
    /// <param name="functionCatalogue">The list of functions encountered.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="emulatorStateSerializationFolder">Where to save the data.</param>
    /// <param name="serializableBreakpointsSource">Source for breakpoints to serialize</param>
    /// <param name="configuration">The emulator configuration, used to derive the program checksum baked into the generated project.</param>
    /// <param name="nodeIndex">The persistent graph index whose poison set is serialized with the CFG reload dump.</param>
    /// <param name="progress">Reports dump phases and steps to the log and to UI subscribers.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    internal EmulationStateDataWriter(State state,
        ExecutionAddressesExtractor executionAddressesExtractor,
        MemoryDataExporter memoryDataExporter,
        ListingExporter listingExporter,
        CfgCpuSnapshotBuilder cfgCpuSnapshotBuilder,
        CfgBlocksJsonExporter cfgBlocksJsonExporter,
        CfgCSharpDumper cfgCSharpDumper,
        ExecutionContextManager executionContextManager,
        FunctionCatalogue functionCatalogue,
        EmulatorStateSerializationFolder emulatorStateSerializationFolder,
        ISerializableBreakpointsSource serializableBreakpointsSource,
        Configuration configuration,
        CfgNodeIndex nodeIndex,
        DumpProgressReporter progress,
        ILogger loggerService) : base(emulatorStateSerializationFolder, loggerService) {
        _executionAddressesExtractor = executionAddressesExtractor;
        _state = state;
        _memoryDataExporter = memoryDataExporter;
        _listingExporter = listingExporter;
        _cfgCpuSnapshotBuilder = cfgCpuSnapshotBuilder;
        _cfgBlocksJsonExporter = cfgBlocksJsonExporter;
        _cfgCSharpDumper = cfgCSharpDumper;
        _executionContextManager = executionContextManager;
        _functionCatalogue = functionCatalogue;
        _serializableBreakpointsSource = serializableBreakpointsSource;
        _configuration = configuration;
        _nodeIndex = nodeIndex;
        _progress = progress;
    }

    /// <summary>
    /// Dumps all recorded data to their respective files.
    /// </summary>
    public void Write() {
        lock (_writeLock) {
            ExecutionAddresses executionAddresses = _executionAddressesExtractor.Extract();
            _progress.BeginDump(DataDirectory);
            bool succeeded = false;
            try {
                WritePhase(DumpPhase.CpuRegisters, CpuRegistersFile, () => File.WriteAllText(CpuRegistersFile, JsonSerializer.Serialize(_state)));
                WritePhase(DumpPhase.Memory, MemoryFile, () => _memoryDataExporter.Write(MemoryFile));
                WritePhase(DumpPhase.Listing, ListingFile, () => _listingExporter.Write(ListingFile));
                if (_configuration.CodeOverridesActive) {
                    // The CFG and execution flow jump between overrides and are not representative of the program, so
                    // the snapshot, CFG/reload/execution-flow dumps and the generated C# are all skipped: building the
                    // snapshot or running the generator here would be wasted work on a non-representative graph.
                    if (LoggerService.IsEnabled(LogLevel.Information)) {
                        LoggerService.LogInformation(
                            "Skipping {CfgBlocksFile}, {CfgPartitionsFile}, {CfgReloadFile}, {ExecutionFlowFile} and generated C#: code overrides are active, so the CFG and execution flow are not representative of the program.",
                            Path.GetFileName(CfgBlocksFile), Path.GetFileName(CfgPartitionsFile), Path.GetFileName(CfgReloadFile), Path.GetFileName(ExecutionFlowFile));
                    }
                } else {
                    _progress.BeginPhase(DumpPhase.CfgSnapshot);
                    CfgCpuSnapshot cfgCpuSnapshot = _cfgCpuSnapshotBuilder.Build(_executionContextManager, null);
                    _progress.EndPhase();
                    // Blocks first: the block graph is always valuable (it is what reload consumes and what an engineer
                    // inspects), so it must be written before the partition overlay, which is derived from it and may be
                    // absent when partitioning fails.
                    WritePhase(DumpPhase.CfgBlocks, CfgBlocksFile, () => _cfgBlocksJsonExporter.WriteBlocks(cfgCpuSnapshot, CfgBlocksFile));
                    WritePhase(DumpPhase.CfgPartitions, CfgPartitionsFile, () => _cfgBlocksJsonExporter.WritePartitions(cfgCpuSnapshot, CfgPartitionsFile));
                    WritePhase(DumpPhase.CfgReload, CfgReloadFile, () => WriteCfgReload(CfgReloadFile));
                    WritePhase(DumpPhase.ExecutionFlow, ExecutionFlowFile, () => new ExecutionAddressesExporter(LoggerService).Write(executionAddresses, ExecutionFlowFile));
                    WriteGeneratedCSharp(cfgCpuSnapshot);
                }
                WritePhase(DumpPhase.Symbols, SymbolsFile, () => new GhidraSymbolsExporter(LoggerService).Write(executionAddresses, _functionCatalogue, SymbolsFile));
                WritePhase(DumpPhase.Breakpoints, BreakpointsFile, () => WriteBreakpoints(BreakpointsFile));
                succeeded = true;
            } finally {
                _progress.EndDump(succeeded);
            }
        }
    }

    private void WriteGeneratedCSharp(CfgCpuSnapshot cfgCpuSnapshot) {
        // The generator must only run on a complete, non-truncated graph. A truncated discovery run has no
        // partitioned program, so the generated-C# dump is skipped rather than aborting the whole state dump.
        // Genuine generation failures on a full graph still propagate loudly out of WritePhase.
        if (cfgCpuSnapshot.PartitionedProgram is null) {
            if (LoggerService.IsEnabled(LogLevel.Information)) {
                LoggerService.LogInformation(
                    "Skipping {FileName}: the CFG graph is truncated, so no partitioned program is available for C# generation.",
                    Path.GetFileName(CfgGeneratedCSharpFile));
            }
            return;
        }

        GeneratedCSharpProgram generated = _cfgCSharpDumper.Generate(cfgCpuSnapshot.PartitionedProgram, _progress);
        WritePhase(DumpPhase.CodeSource, CfgGeneratedCSharpFile, () => _cfgCSharpDumper.Write(generated, CfgGeneratedCSharpFile));
        WritePhase(DumpPhase.CodeProject, GeneratedProjectDirectory, () => _cfgCSharpDumper.WriteProject(
            cfgCpuSnapshot.PartitionedProgram, generated, GeneratedProjectDirectory, ComputeExpectedChecksum()));
    }

    /// <summary>
    /// Uppercase SHA-256 hex of the emulated program, baked into the generated project's Program.cs so the
    /// overrides cannot be replayed against a different binary. Matches the format Spice86 dump folders use
    /// (<see cref="ConvertUtils.ByteArrayToHexString"/>) and is accepted case-insensitively by
    /// <c>--ExpectedChecksum</c>.
    /// </summary>
    private string ComputeExpectedChecksum() {
        return ConvertUtils.ByteArrayToHexString(SHA256.HashData(File.ReadAllBytes(_configuration.Exe)));
    }

    private void WritePhase(DumpPhase phase, string path, Action writeAction) {
        _progress.BeginPhase(phase, Path.GetFileName(path));
        writeAction();
        _progress.EndPhase();
    }

    private void WriteCfgReload(string filePath) {
        CfgReloadDump dump = new CfgReloadExporter().Export(_executionContextManager, _nodeIndex.PoisonSet);
        string jsonString = JsonSerializer.Serialize(dump, CfgReloadSerialization.Options);
        File.WriteAllText(filePath, jsonString);
    }

    private void WriteBreakpoints(string filePath) {
        SerializableUserBreakpointCollection serializedBreakpoints =
            _serializableBreakpointsSource.CreateSerializableBreakpoints();

        string jsonString = JsonSerializer.Serialize(serializedBreakpoints,
            new JsonSerializerOptions { WriteIndented = true });
        using FileStream fileStream = File.Open(filePath, FileMode.Create);
        fileStream.Write(Encoding.UTF8.GetBytes(jsonString));
    }
}