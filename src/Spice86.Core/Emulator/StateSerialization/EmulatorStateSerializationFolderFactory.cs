namespace Spice86.Core.Emulator.StateSerialization;

using Serilog.Events;

using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Security.Cryptography;

/// <summary>
/// Computes context information for saving or loading emulator state.
/// </summary>
public class EmulatorStateSerializationFolderFactory(ILoggerService loggerService) {
    /// <summary>
    /// Creates an instance of <see cref="EmulatorStateSerializationFolder"/>.
    /// The folder is created on the disk either from configuration, from env var or from current path. 
    /// </summary>
    /// <param name="exePath">Path to the executable file.</param>
    /// <param name="explicitDataDirectory">Optional explicit data directory from command line.</param>
    /// <exception cref="ArgumentException">Thrown when exePath is null or whitespace.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the executable file doesn't exist.</exception>
    public EmulatorStateSerializationFolder ComputeFolder(string exePath, string? explicitDataDirectory) {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);

        if (!File.Exists(exePath)) {
            throw new FileNotFoundException($"Executable file not found: {exePath}", exePath);
        }

        string programHash = ComputeProgramHash(exePath);
        string baseDirectory = DetermineBaseDirectory(explicitDataDirectory);
        // Always append program hash as subdirectory to isolate dumps per executable
        string dataDirectory = Path.GetFullPath(Path.Join(baseDirectory, programHash));
        CreateIfNotExist(dataDirectory);
        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Data folder for program hash {ProgramHash} is here {DumpDirectory}",
                programHash, dataDirectory);
        }

        return new(dataDirectory);
    }

    private void CreateIfNotExist(string directoryPath) {
        if (!Directory.Exists(directoryPath)) {
            Directory.CreateDirectory(directoryPath);
        }
    }

    private string ComputeProgramHash(string exePath) {
        byte[] fileData = File.ReadAllBytes(exePath);
        byte[] hashBytes = SHA256.HashData(fileData);
        return ConvertUtils.ByteArrayToHexString(hashBytes);
    }

    private string DetermineBaseDirectory(string? explicitDirectory) {
        // Priority 1: Explicit directory from command line
        if (!string.IsNullOrWhiteSpace(explicitDirectory)) {
            return explicitDirectory;
        }

        // Priority 2: SPICE86_DUMPS_FOLDER environment variable (if directory exists)
        string? envFolder = Environment.GetEnvironmentVariable("SPICE86_DUMPS_FOLDER");
        if (!string.IsNullOrWhiteSpace(envFolder) && Directory.Exists(envFolder)) {
            return envFolder;
        }

        // Priority 3: Current directory
        return ".";
    }
}