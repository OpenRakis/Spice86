namespace Spice86.Core.Emulator.StateSerialization;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Shared.Emulator.VM.Breakpoint.Serializable;
using Spice86.Shared.Interfaces;

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
    private readonly FunctionCatalogue _functionCatalogue;
    private readonly ISerializableBreakpointsSource _serializableBreakpointsSource;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="executionAddressesExtractor">The class that dumps machine code execution flow.</param>
    /// <param name="memoryDataExporter">The class used to dump main memory data properly.</param>
    /// <param name="listingExporter">The class used to dump asm listing of encountered instructions.</param>
    /// <param name="functionCatalogue">The list of functions encountered.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="emulatorStateSerializationFolder">Where to save the data.</param>
    /// <param name="serializableBreakpointsSource">Source for breakpoints to serialize</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public EmulationStateDataWriter(State state,
        ExecutionAddressesExtractor executionAddressesExtractor,
        MemoryDataExporter memoryDataExporter,
        ListingExporter listingExporter,
        FunctionCatalogue functionCatalogue,
        EmulatorStateSerializationFolder emulatorStateSerializationFolder,
        ISerializableBreakpointsSource serializableBreakpointsSource,
        ILoggerService loggerService) : base(emulatorStateSerializationFolder, loggerService) {
        _executionAddressesExtractor = executionAddressesExtractor;
        _state = state;
        _memoryDataExporter = memoryDataExporter;
        _listingExporter = listingExporter;
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
        File.WriteAllText(CpuRegistersFile, JsonSerializer.Serialize(_state));
        _memoryDataExporter.Write(MemoryFile);
        _listingExporter.Write(ListingFile);
        ExecutionAddresses executionAddresses = _executionAddressesExtractor.Extract();
        new GhidraSymbolsExporter(LoggerService).Write(executionAddresses, _functionCatalogue, SymbolsFile);
        new ExecutionAddressesExporter(LoggerService).Write(executionAddresses, ExecutionFlowFile);
        WriteBreakpoints(BreakpointsFile);
    }
    
    
    private void WriteBreakpoints(string filePath) {
        SerializableUserBreakpointCollection serializedBreakpoints =
            _serializableBreakpointsSource.CreateSerializableBreakpoints();

        string jsonString = JsonSerializer.Serialize(serializedBreakpoints,
            new JsonSerializerOptions { WriteIndented = true });
        using FileStream fileStream = File.Open(filePath, FileMode.Create);
        fileStream.Write(Encoding.UTF8.GetBytes(jsonString));

        if (LoggerService.IsEnabled(LogEventLevel.Information)) {
            LoggerService.Information("Saved {Count} breakpoints to {FilePath}",
                serializedBreakpoints.Breakpoints.Count, filePath);
        }
    }
}