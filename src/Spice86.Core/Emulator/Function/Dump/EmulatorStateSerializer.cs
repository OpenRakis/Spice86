namespace Spice86.Core.Emulator.Function.Dump;

using Spice86.Core.Emulator.CPU;
using Spice86.Shared.Emulator.VM.Breakpoint.Serializable;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>
/// This class is responsible for serializing the emulator state to a directory.
/// </summary>
public class EmulatorStateSerializer {
    private readonly State _state;
    private readonly IExecutionDumpFactory _executionDumpFactory;
    private readonly FunctionCatalogue _functionCatalogue;
    private readonly ILoggerService _loggerService;
    private readonly MemoryDataExporter _memoryDataExporter;

    private const string BreakpointsFileName = "Breakpoints.json";
    private readonly ISerializableBreakpointsSource _serializableBreakpointsSource;
    private readonly string _programHash;


    /// <summary>
    /// Initializes a new instance of <see cref="EmulatorStateSerializer"/>.
    /// </summary>
    public EmulatorStateSerializer(Configuration configuration,
        MemoryDataExporter memoryDataExporter, 
        State state, IExecutionDumpFactory executionDumpFactory,
        FunctionCatalogue functionCatalogue,
        ISerializableBreakpointsSource serializableBreakpointsSource,
        ILoggerService loggerService) {
        
        _state = state;
        _memoryDataExporter = memoryDataExporter;
        _executionDumpFactory = executionDumpFactory;
        _functionCatalogue = functionCatalogue;
        _loggerService = loggerService;
        _serializableBreakpointsSource = serializableBreakpointsSource;
        _programHash = GetProgramHash(configuration);
    }

    /// <summary>
    /// Dumps the emulator state to the specified directory.
    /// </summary>
    public void SerializeEmulatorStateToDirectory(string dirPath) {
        new RecordedDataWriter(
                _state,
                _executionDumpFactory,
                _memoryDataExporter,
                _functionCatalogue,
                dirPath, 
                _loggerService)
            .DumpAll();
        SaveBreakpoints(dirPath);
    }


    private string GetProgramHash(Configuration configuration) {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.Exe);
        if (!File.Exists(configuration.Exe)) {
            throw new FileNotFoundException(configuration.Exe);
        }
        return ConvertUtils.ByteArrayToHexString(SHA256.HashData(File.ReadAllBytes(configuration.Exe)));
    }

    private void SaveBreakpoints(string dirPath) {
        string fileName = ComputeFileName(dirPath);
        CreateDirIfNotExist(fileName);
        SerializeBreakpoints(fileName);
    }

    private static void CreateDirIfNotExist(string fileName) {
        string? dir = Path.GetDirectoryName(fileName);
        if (!string.IsNullOrWhiteSpace(dir) &&
            !Directory.Exists(dir)) {
            Directory.CreateDirectory(dir);
        }
    }

    private string ComputeFileName(string dirPath) {
        return Path.Join(dirPath, _programHash.ToString(), BreakpointsFileName);
    }

    private void SerializeBreakpoints(string filePath) {
        try {
            if (Directory.Exists(Path.GetDirectoryName(filePath))) {
                SerializableUserBreakpointCollection serializedBreakpoints =
                    _serializableBreakpointsSource.CreateSerializableBreakpoints();

                ProgramSerializableBreakpoints programSerializedBreakpoints = new() {
                    ProgramHash = _programHash,
                    SerializedBreakpoints = serializedBreakpoints
                };

                string jsonString = JsonSerializer.Serialize(programSerializedBreakpoints,
                    new JsonSerializerOptions { WriteIndented = true });
                using FileStream fileStream = File.Open(filePath, FileMode.Create);
                fileStream.Write(Encoding.UTF8.GetBytes(jsonString));

                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                    _loggerService.Information("Saved {Count} breakpoints for program {ProgramHash} to {FilePath}",
                        serializedBreakpoints.Breakpoints.Count, _programHash, filePath);
                }
            }
        } catch (Exception ex) {
            _loggerService.Error(ex, "Failed to save breakpoints to {FilePath}", filePath);
        }
    }

    public SerializableUserBreakpointCollection LoadBreakpoints(string dirPath) {
        string fileName = ComputeFileName(dirPath);
        return DeserializeBreakpoints(fileName);
    }

    private SerializableUserBreakpointCollection DeserializeBreakpoints(string filePath) {
        try {
            if (!File.Exists(filePath) || new FileInfo(filePath).Length == 0) {
                return new();
            }

            string jsonString = File.ReadAllText(filePath);

            if (string.IsNullOrWhiteSpace(jsonString)) {
                return new();
            }

            ProgramSerializableBreakpoints? programSerializedBreakpoints = JsonSerializer.Deserialize<ProgramSerializableBreakpoints>(jsonString);

            if (programSerializedBreakpoints == null) {
                return new();
            }

            if (!programSerializedBreakpoints.ProgramHash.AsSpan().SequenceEqual(_programHash)) {
                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                    _loggerService.Error("Breakpoints on disk were for program {LoadedHash} but current program is {CurrentHash}",
                        programSerializedBreakpoints.ProgramHash, _programHash);
                }
                return new();
            }

            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _loggerService.Information("Loaded {Count} breakpoints for program {ProgramHash}",
                    programSerializedBreakpoints.SerializedBreakpoints.Breakpoints.Count, _programHash);
            }

            return programSerializedBreakpoints.SerializedBreakpoints;
        } catch (Exception ex) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _loggerService.Error(ex, "Failed to load breakpoints from {FilePath}", filePath);
            }
        }

        return new();
    }
}