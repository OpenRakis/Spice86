namespace Spice86.Core.Emulator.StateSerialization;

using Serilog.Events;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph;
using Spice86.Core.Emulator.StateSerialization.CfgReload;
using Spice86.Shared.Emulator.VM.Breakpoint.Serializable;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
        ILoggerService loggerService) : base(emulatorStateSerializationFolder, loggerService) {
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
    }

    /// <summary>
    /// Dumps all recorded data to their respective files.
    /// </summary>
    public void Write() {
        if (LoggerService.IsEnabled(LogEventLevel.Information)) {
            LoggerService.Information("Saving all data to {DumpDirectory}", DataDirectory);
        }
        ExecutionAddresses executionAddresses = _executionAddressesExtractor.Extract();
        WriteToFile(CpuRegistersFile, () => File.WriteAllText(CpuRegistersFile, JsonSerializer.Serialize(_state)));
        WriteToFile(MemoryFile, () => _memoryDataExporter.Write(MemoryFile));
        WriteToFile(ListingFile, () => _listingExporter.Write(ListingFile));
        if (_configuration.CodeOverridesActive) {
            // The CFG and execution flow jump between overrides and are not representative of the program, so
            // the snapshot, CFG/reload/execution-flow dumps and the generated C# are all skipped: building the
            // snapshot or running the generator here would be wasted work on a non-representative graph.
            if (LoggerService.IsEnabled(LogEventLevel.Information)) {
                LoggerService.Information(
                    "Skipping {CfgBlocksFile}, {CfgPartitionsFile}, {CfgReloadFile}, {ExecutionFlowFile} and generated C#: code overrides are active, so the CFG and execution flow are not representative of the program.",
                    Path.GetFileName(CfgBlocksFile), Path.GetFileName(CfgPartitionsFile), Path.GetFileName(CfgReloadFile), Path.GetFileName(ExecutionFlowFile));
            }
        } else {
            CfgCpuSnapshot cfgCpuSnapshot = _cfgCpuSnapshotBuilder.Build(_executionContextManager, null);
            // Blocks first: the block graph is always valuable (it is what reload consumes and what an engineer
            // inspects), so it must be written before the partition overlay, which is derived from it and may be
            // absent when partitioning fails.
            WriteToFile(CfgBlocksFile, () => _cfgBlocksJsonExporter.WriteBlocks(cfgCpuSnapshot, CfgBlocksFile));
            WriteToFile(CfgPartitionsFile, () => _cfgBlocksJsonExporter.WritePartitions(cfgCpuSnapshot, CfgPartitionsFile));
            WriteToFile(CfgReloadFile, () => WriteCfgReload(CfgReloadFile));
            WriteToFile(ExecutionFlowFile, () => new ExecutionAddressesExporter(LoggerService).Write(executionAddresses, ExecutionFlowFile));
            WriteGeneratedCSharp(cfgCpuSnapshot);
        }
        WriteToFile(SymbolsFile, () => new GhidraSymbolsExporter(LoggerService).Write(executionAddresses, _functionCatalogue, SymbolsFile));
        WriteToFile(BreakpointsFile, () => WriteBreakpoints(BreakpointsFile));
    }

    private void WriteGeneratedCSharp(CfgCpuSnapshot cfgCpuSnapshot) {
        // The generator must only run on a complete, non-truncated graph. A truncated discovery run has no
        // partitioned program, so the generated-C# dump is skipped rather than aborting the whole state dump.
        // Genuine generation failures on a full graph still propagate loudly out of WriteToFile.
        if (cfgCpuSnapshot.PartitionedProgram is null) {
            if (LoggerService.IsEnabled(LogEventLevel.Information)) {
                LoggerService.Information(
                    "Skipping {FileName}: the CFG graph is truncated, so no partitioned program is available for C# generation.",
                    Path.GetFileName(CfgGeneratedCSharpFile));
            }
            return;
        }

        WriteToFile(CfgGeneratedCSharpFile, () => _cfgCSharpDumper.Write(cfgCpuSnapshot.PartitionedProgram, CfgGeneratedCSharpFile));
        WriteToFile(GeneratedProjectDirectory, () => _cfgCSharpDumper.WriteProject(
            cfgCpuSnapshot.PartitionedProgram, GeneratedProjectDirectory, ComputeExpectedChecksum()));
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

    private void WriteToFile(string path, Action writeAction) {
        if (LoggerService.IsEnabled(LogEventLevel.Information)) {
            LoggerService.Information("Saving file {FileName}", Path.GetFileName(path));
        }
        writeAction();
        if (LoggerService.IsEnabled(LogEventLevel.Information)) {
            LoggerService.Information("Saved file {FileName}", Path.GetFileName(path));
        }
    }

    private void WriteCfgReload(string filePath) {
        CfgReloadDump dump = new CfgReloadExporter().Export(_executionContextManager);
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