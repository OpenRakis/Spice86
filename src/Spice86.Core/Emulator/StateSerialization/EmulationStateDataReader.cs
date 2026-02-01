namespace Spice86.Core.Emulator.StateSerialization;

using Spice86.Core.Emulator.Function;
using Spice86.Shared.Emulator.VM.Breakpoint.Serializable;
using Spice86.Shared.Interfaces;

using System.Text.Json;

/// <summary>
/// A class that provides methods for reading recorded data from files.
/// </summary>
public class EmulationStateDataReader : EmulationStateDataIoHandler {
    /// <summary>
    /// Initializes a new instance of the <see cref="EmulationStateDataReader"/> class.
    /// </summary>
    /// <param name="emulatorStateSerializationFolder">Where to read the data from.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public EmulationStateDataReader(EmulatorStateSerializationFolder emulatorStateSerializationFolder, ILoggerService loggerService) : base(emulatorStateSerializationFolder, loggerService) {
    }

    /// <summary>
    /// Reads the execution flow recorder data from a file or creates a new one if the file does not exist.
    /// </summary>
    /// <returns>The execution data  read from the file, or a new instance if the file does not exist.</returns>
    public ExecutionAddresses ReadExecutionDataFromFileOrCreate() {
           return new ExecutionAddressesExporter(LoggerService)
                .ReadFromFileOrCreate(ExecutionFlowFile);
    }

    /// <summary>
    /// Reads the Ghidra symbols data from a file or creates a new one if the file does not exist.
    /// </summary>
    /// <returns>A list of function information, read from the file or a new instance if the file does not exist.</returns>
    public IEnumerable<FunctionInformation> ReadGhidraSymbolsFromFileOrCreate() {
        return new GhidraSymbolsExporter(LoggerService).ReadFromFileOrCreate(SymbolsFile);
    }
    
    public SerializableUserBreakpointCollection ReadBreakpointsFromFileOrCreate() {
        if (!File.Exists(BreakpointsFile) || new FileInfo(BreakpointsFile).Length == 0) {
            return new();
        }

        string jsonString = File.ReadAllText(BreakpointsFile);

        if (string.IsNullOrWhiteSpace(jsonString)) {
            return new();
        }

        SerializableUserBreakpointCollection? res = JsonSerializer.Deserialize<SerializableUserBreakpointCollection>(jsonString);

        if (res == null) {
            return new();
        }

        if (LoggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            LoggerService.Information("Loaded {Count} breakpoints", res.Breakpoints.Count);
        }

        return res;
    }
}
