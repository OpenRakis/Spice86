namespace Spice86.Core.Emulator.StateSerialization;

using Serilog.Events;

using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Interfaces;

using System.IO;
using System.Text.Json;

/// <summary>
/// Provides functionality for dumping and reading execution flow data to and from a file.
/// </summary>
public class ExecutionAddressesExporter {
    private readonly ILoggerService _loggerService;

    /// <summary>
    /// Creates a new ExecutionFlowDumper instance.
    /// </summary>
    /// <param name="loggerService">The logger service implementation.</param>
    public ExecutionAddressesExporter(ILoggerService loggerService) {
        _loggerService = loggerService;
    }

    /// <summary>
    /// Dumps the execution flow recorded by the provided executionDump to a JSON file at the specified <paramref name="destinationFilePath"/>
    /// </summary>
    /// <param name="executionAddresses">The execution dump to dump.</param>
    /// <param name="destinationFilePath">The path to the destination file to create and write the execution flow data to.</param>
    public void Write(ExecutionAddresses executionAddresses, string destinationFilePath) {
        using StreamWriter printWriter = new StreamWriter(destinationFilePath);
        string jsonString = JsonSerializer.Serialize(executionAddresses);
        printWriter.WriteLine(jsonString);
    }

    /// <summary>
    /// Reads the execution flow data from a JSON file at the specified <paramref name="filePath"/> or creates a new execution dump if the file does not exist.
    /// </summary>
    /// <param name="filePath">The path to the execution flow data file to read.</param>
    /// <returns>An <see cref="ExecutionAddresses"/> instance containing the data read from the file or a new <see cref="ExecutionAddresses"/> object if the file does not exist.</returns>
    /// <exception cref="UnrecoverableException">Thrown if the file at the specified <paramref name="filePath"/> is not valid JSON.</exception>
    public ExecutionAddresses ReadFromFileOrCreate(string filePath) {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) {
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("File path \"{FilePath}\" is blank or doesn't exist", filePath);
            }
            return new ();
        }
        try {
            return JsonSerializer.Deserialize<ExecutionAddresses>(File.ReadAllText(filePath)) ?? new();
        } catch (JsonException e) {
            throw new UnrecoverableException($"File {filePath} is not valid", e);
        }
    }
}