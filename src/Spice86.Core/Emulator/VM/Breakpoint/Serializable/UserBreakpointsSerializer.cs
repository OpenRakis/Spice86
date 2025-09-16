namespace Spice86.Core.Emulator.VM.Breakpoint.Serializable;

using Spice86.Shared.Interfaces;
using Spice86.Shared.Emulator.VM.Breakpoint.Serializable;
using Spice86.Core.CLI;
using Spice86.Shared.Utils;

using System;
using System.Text.Json;
using System.Text;
using System.IO;
using System.Security.Cryptography;

/// <summary>
/// Serializes (on startup) and deserialize (on app shutdown) breakpoints created by the user in the internal Spice86 debugger UI.
/// </summary>
public class UserBreakpointsSerializer {
    private const string BreakpointsFileNameFormat = "Breakpoints_{0}.json";
    private readonly ILoggerService _loggerService;
    private readonly ISerializableBreakpointsSource _serializableBreakpointsSource;
    private readonly string _programHash;

    public UserBreakpointsSerializer(Configuration configuration, ILoggerService loggerService,
        ISerializableBreakpointsSource serializableBreakpointsSource) {
        _loggerService = loggerService;
        _serializableBreakpointsSource = serializableBreakpointsSource;
        _programHash = GetProgramHash(configuration);
    }

    private string GetProgramHash(Configuration configuration) {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.Exe);
        if(!File.Exists(configuration.Exe)) {
            throw new FileNotFoundException(configuration.Exe);
        }
        return ConvertUtils.ByteArrayToHexString(SHA256.HashData(File.ReadAllBytes(configuration.Exe)));
    }

    public void SaveBreakpoints(string dirPath) {
        string fileName = string.Format(BreakpointsFileNameFormat, _programHash);
        SerializeBreakpoints(Path.Join(dirPath, fileName));
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
        string fileName = string.Format(BreakpointsFileNameFormat, _programHash);
        return DeserializeBreakpoints(Path.Combine(dirPath, fileName));
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
            if(_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _loggerService.Error(ex, "Failed to load breakpoints from {FilePath}", filePath);
            }
        }

        return new();
    }
}
