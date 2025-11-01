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
    /// Always contains a subdirectory named with the program hash to isolate dumps per executable.
    /// Base directory priority:
    /// 1. Explicit directory from command line (RecordedDataDirectory)
    /// 2. SPICE86_DUMPS_FOLDER environment variable (if it exists as a directory)
    /// 3. Current directory
    /// Final path is always: {BaseDirectory}/{ProgramHash}/
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
        string baseDirectory;
        
        // Priority 1: Explicit directory from command line
        if (!string.IsNullOrWhiteSpace(explicitDirectory)) {
            baseDirectory = explicitDirectory;
        }
        // Priority 2: SPICE86_DUMPS_FOLDER environment variable (if directory exists)
        else {
            string? envFolder = Environment.GetEnvironmentVariable("SPICE86_DUMPS_FOLDER");
            if (!string.IsNullOrWhiteSpace(envFolder) && Directory.Exists(envFolder)) {
                baseDirectory = envFolder;
            } else {
                // Priority 3: Current directory
                baseDirectory = ".";
            }
        }

        // Always append program hash as subdirectory to isolate dumps per executable
        return Path.Join(baseDirectory, ProgramHash);
    }
}
