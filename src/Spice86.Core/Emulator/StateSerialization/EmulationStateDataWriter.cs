namespace Spice86.Core.Emulator.StateSerialization;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.Function;
using Spice86.Shared.Emulator.VM.Breakpoint.Serializable;
using Spice86.Shared.Interfaces;

using System.IO;
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
    private readonly CfgBlocksJsonExporter _cfgBlocksJsonExporter;
    private readonly ExecutionContextManager _executionContextManager;
    private readonly FunctionCatalogue _functionCatalogue;
    private readonly ISerializableBreakpointsSource _serializableBreakpointsSource;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="executionAddressesExtractor">The class that dumps machine code execution flow.</param>
    /// <param name="memoryDataExporter">The class used to dump main memory data properly.</param>
    /// <param name="listingExporter">The class used to dump asm listing of encountered instructions.</param>
    /// <param name="cfgBlocksJsonExporter">The class used to dump the CFG block graph as JSON.</param>
    /// <param name="executionContextManager">The execution context manager for CFG graph export.</param>
    /// <param name="functionCatalogue">The list of functions encountered.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="emulatorStateSerializationFolder">Where to save the data.</param>
    /// <param name="serializableBreakpointsSource">Source for breakpoints to serialize</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public EmulationStateDataWriter(State state,
        ExecutionAddressesExtractor executionAddressesExtractor,
        MemoryDataExporter memoryDataExporter,
        ListingExporter listingExporter,
        CfgBlocksJsonExporter cfgBlocksJsonExporter,
        ExecutionContextManager executionContextManager,
        FunctionCatalogue functionCatalogue,
        EmulatorStateSerializationFolder emulatorStateSerializationFolder,
        ISerializableBreakpointsSource serializableBreakpointsSource,
        ILoggerService loggerService) : base(emulatorStateSerializationFolder, loggerService) {
        _executionAddressesExtractor = executionAddressesExtractor;
        _state = state;
        _memoryDataExporter = memoryDataExporter;
        _listingExporter = listingExporter;
        _cfgBlocksJsonExporter = cfgBlocksJsonExporter;
        _executionContextManager = executionContextManager;
        _functionCatalogue = functionCatalogue;
        _serializableBreakpointsSource = serializableBreakpointsSource;
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
        WriteToFile(CfgBlocksFile, () => _cfgBlocksJsonExporter.Write(_executionContextManager, CfgBlocksFile));
        WriteToFile(SymbolsFile, () => new GhidraSymbolsExporter(LoggerService).Write(executionAddresses, _functionCatalogue, SymbolsFile));
        WriteToFile(ExecutionFlowFile, () => new ExecutionAddressesExporter(LoggerService).Write(executionAddresses, ExecutionFlowFile));
        WriteToFile(BreakpointsFile, () => WriteBreakpoints(BreakpointsFile));
    }

    private void WriteToFile(string path, Action writeAction) {
        if (LoggerService.IsEnabled(LogEventLevel.Information)) {
            LoggerService.Information("Saving file {FileName}", Path.GetFileName(path));
        }
        writeAction();
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