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
/// Serializes (on startup) and deserialize (on app shutdown) internal debugger breakpoints from disk.
/// </summary>
public class EmulatorBreakpointsSerializer {
    private const string BreakpointsFileNameFormat = "Breakpoints_{0}.json";
    private readonly Configuration _configuration;
    private readonly ILoggerService _loggerService;
    private ISerializableBreakpointsHolder? _serializableBreakpointsHolder = null;
    private readonly string _programHash;

    public EmulatorBreakpointsSerializer(Configuration configuration, ILoggerService loggerService) {
        _configuration = configuration;
        _loggerService = loggerService;
        _programHash = GetProgramHash(configuration);
    }

    private string GetProgramHash(Configuration configuration) {
        if (configuration.ExpectedChecksumValue != null && configuration.ExpectedChecksumValue.Length > 0) {
            return ConvertUtils.ByteArrayToHexString(configuration.ExpectedChecksumValue);
        }

        // If no hash is specified,compute it ourselves
        if (string.IsNullOrWhiteSpace(configuration.Exe) || !File.Exists(configuration.Exe)) {
            throw new FileNotFoundException(configuration.Exe);
        }
        return ConvertUtils.ByteArrayToHexString(SHA256.HashData(File.ReadAllBytes(configuration.Exe)));
    }

    public void AddSerializableBreakpointsHolder(ISerializableBreakpointsHolder serializableBreakpointsHolder) {
        _serializableBreakpointsHolder = serializableBreakpointsHolder;
    }

    public void SaveBreakpoints() {
        string fileName = string.Format(BreakpointsFileNameFormat, _programHash);
        SerializeBreakpoints(Path.Combine(_configuration.RecordedDataDirectory, fileName));
    }

    private void SerializeBreakpoints(string filePath) {
        try {
            if (Directory.Exists(Path.GetDirectoryName(filePath)) && _serializableBreakpointsHolder != null) {
                SerializedBreakpoints serializedBreakpoints = _serializableBreakpointsHolder.CreateSerializableBreakpoints();
                ProgramSerializedBreakpoints programSerializedBreakpoints = new() {
                    ProgramHash = _programHash,
                    SerializedBreakpoints = serializedBreakpoints
                };

                string jsonString = JsonSerializer.Serialize(programSerializedBreakpoints, new JsonSerializerOptions { WriteIndented = true });
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

    public SerializedBreakpoints LoadBreakpoints() {
        string fileName = string.Format(BreakpointsFileNameFormat, _programHash);
        return DeserializeBreakpoints(Path.Combine(_configuration.RecordedDataDirectory, fileName));
    }

    private SerializedBreakpoints DeserializeBreakpoints(string filePath) {
        if (!File.Exists(filePath) || new FileInfo(filePath).Length == 0) {
            return new();
        }

        try {
            string jsonString = File.ReadAllText(filePath);

            if (string.IsNullOrWhiteSpace(jsonString)) {
                return new();
            }

            ProgramSerializedBreakpoints? programSerializedBreakpoints = JsonSerializer.Deserialize<ProgramSerializedBreakpoints>(jsonString);

            if (programSerializedBreakpoints == null) {
                return new();
            }

            if (!programSerializedBreakpoints.ProgramHash.AsSpan().SequenceEqual(_programHash)) {
                _loggerService.Warning("Breakpoints on disk were for program {LoadedHash} but current program is {CurrentHash}",
                    programSerializedBreakpoints.ProgramHash, _programHash);
                return new();
            }

            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _loggerService.Information("Loaded {Count} breakpoints for program {ProgramHash}",
                    programSerializedBreakpoints.SerializedBreakpoints.Breakpoints.Count, _programHash);
            }

            return programSerializedBreakpoints.SerializedBreakpoints;
        } catch (Exception ex) {
            _loggerService.Error(ex, "Failed to load breakpoints from {FilePath}", filePath);
        }

        return new();
    }
}
