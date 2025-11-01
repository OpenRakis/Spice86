namespace Spice86.Core.Emulator.Function.Dump;

using Spice86.Shared.Utils;

using System.Security.Cryptography;

/// <summary>
/// Contains computed context information for dumping emulator state.
/// This includes the program hash and the directory where dumps should be written.
/// </summary>
public class DumpContext {
    /// <summary>
    /// Gets the SHA-256 hash of the program executable.
    /// </summary>
    public string ProgramHash { get; }

    /// <summary>
    /// Gets the directory where dumps should be written.
    /// Priority order:
    /// 1. Explicit directory from command line (RecordedDataDirectory)
    /// 2. SPICE86_DUMPS_FOLDER environment variable (if it exists as a directory)
    /// 3. Subdirectory named with the program hash
    /// </summary>
    public string DumpDirectory { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DumpContext"/> class.
    /// </summary>
    /// <param name="exePath">Path to the executable file.</param>
    /// <param name="explicitDumpDirectory">Optional explicit dump directory from command line.</param>
    /// <exception cref="ArgumentException">Thrown when exePath is null or whitespace.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the executable file doesn't exist.</exception>
    public DumpContext(string? exePath, string? explicitDumpDirectory) {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        
        if (!File.Exists(exePath)) {
            throw new FileNotFoundException($"Executable file not found: {exePath}", exePath);
        }

        ProgramHash = ComputeProgramHash(exePath);
        DumpDirectory = DetermineDumpDirectory(explicitDumpDirectory);
    }

    private string ComputeProgramHash(string exePath) {
        byte[] fileData = File.ReadAllBytes(exePath);
        byte[] hashBytes = SHA256.HashData(fileData);
        return ConvertUtils.ByteArrayToHexString(hashBytes);
    }

    private string DetermineDumpDirectory(string? explicitDirectory) {
        // Priority 1: Explicit directory from command line
        if (!string.IsNullOrWhiteSpace(explicitDirectory)) {
            return explicitDirectory;
        }

        // Priority 2: SPICE86_DUMPS_FOLDER environment variable (if directory exists)
        string? envFolder = Environment.GetEnvironmentVariable("SPICE86_DUMPS_FOLDER");
        if (!string.IsNullOrWhiteSpace(envFolder) && Directory.Exists(envFolder)) {
            return envFolder;
        }

        // Priority 3: Subdirectory named with program hash
        return ProgramHash;
    }
}
