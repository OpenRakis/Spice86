namespace Spice86.Tests.CpuTests.SingleStepTests;

using System.IO.Compression;

/// <summary>
/// Helper class for reading and writing revocation lists.
/// </summary>
public class RevocationListHelper {
    private readonly string _readBasePath;
    private readonly string _writeBasePath;

    /// <summary>
    /// Creates a new RevocationListHelper with configurable paths.
    /// </summary>
    /// <param name="readBasePath">Base path for reading revocation list files (default: "Resources/cpuTests/singleStepTests")</param>
    /// <param name="writeBasePath">Base path for writing revocation list files (default: current directory)</param>
    public RevocationListHelper(string readBasePath = "Resources/cpuTests/singleStepTests", string? writeBasePath = null) {
        _readBasePath = readBasePath;
        _writeBasePath = writeBasePath ?? Environment.CurrentDirectory;
    }

    /// <summary>
    /// Reads and combines revocation lists from both a zip archive and a global file.
    /// </summary>
    /// <param name="archive">The zip archive containing the revocation_list.txt file</param>
    /// <param name="cpuModelString">The CPU model string (e.g., "80386")</param>
    /// <param name="range">The opcode range (e.g., "00-65")</param>
    /// <returns>A combined set of test hashes that should be skipped</returns>
    public ISet<string> ReadCombinedRevocationLists(ZipArchive archive, string cpuModelString, string range) {
        HashSet<string> combined = new HashSet<string>();

        // Read from zip archive
        ISet<string> zipRevocationList = ReadRevocationListFromZip(archive);
        foreach (string hash in zipRevocationList) {
            combined.Add(hash);
        }

        // Read from global file
        string fileName = $"revocation_list_{cpuModelString}_{range.Replace(".", "_")}.txt";
        string globalFilePath = Path.Combine(_readBasePath, fileName);
        ISet<string> globalRevocationList = ReadRevocationListFromFile(globalFilePath);
        foreach (string hash in globalRevocationList) {
            combined.Add(hash);
        }

        return combined;
    }

    /// <summary>
    /// Reads the revocation list from a zip archive entry.
    /// Skips comments (lines starting with #) and empty lines.
    /// </summary>
    /// <param name="archive">The zip archive containing the revocation_list.txt file</param>
    /// <returns>A set of test hashes that should be skipped</returns>
    public ISet<string> ReadRevocationListFromZip(ZipArchive archive) {
        ZipArchiveEntry? revocationEntry = archive.GetEntry("revocation_list.txt");
        if (revocationEntry is null) {
            return new HashSet<string>();
        }
        using Stream entryStream = revocationEntry.Open();
        return ReadRevocationListFromStream(entryStream);
    }

    /// <summary>
    /// Reads the revocation list from a file.
    /// Skips comments (lines starting with #) and empty lines.
    /// </summary>
    /// <param name="filePath">The path to the revocation list file</param>
    /// <returns>A set of test hashes that should be skipped</returns>
    /// <exception cref="FileNotFoundException">Thrown when the revocation list file does not exist</exception>
    public ISet<string> ReadRevocationListFromFile(string filePath) {
        if (!File.Exists(filePath)) {
            throw new FileNotFoundException($"Revocation list file not found: {filePath}");
        }
        using FileStream fileStream = File.OpenRead(filePath);
        return ReadRevocationListFromStream(fileStream);
    }

    /// <summary>
    /// Reads the revocation list from a stream.
    /// Skips comments (lines starting with #) and empty lines.
    /// </summary>
    /// <param name="stream">The stream to read from</param>
    /// <returns>A set of test hashes that should be skipped</returns>
    private ISet<string> ReadRevocationListFromStream(Stream stream) {
        using StreamReader reader = new StreamReader(stream);
        HashSet<string> revocationList = [];
        string? line;
        while ((line = reader.ReadLine()) != null) {
            string trimmedLine = line.Trim();
            // Skip empty lines and comments (lines starting with #)
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith('#')) {
                continue;
            }
            revocationList.Add(trimmedLine);
        }
        return revocationList;
    }

    /// <summary>
    /// Writes test statistics to a revocation list file.
    /// </summary>
    /// <param name="stats">The statistics to write</param>
    /// <param name="cpuModelString">The CPU model string (e.g., "80386")</param>
    /// <param name="range">The opcode range (e.g., "00-65")</param>
    public void WriteRevocationList(TestStatistics stats, string cpuModelString, string range) {
        string outputPath = GetRevocationListPath(cpuModelString, range);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using StreamWriter writer = new StreamWriter(outputPath);

        int totalTests = stats.TotalCounters.Total;

        writer.WriteLine($"# Revocation list generated for {cpuModelString} range {range}");
        writer.WriteLine($"# Total tests: {totalTests}");
        writer.WriteLine($"# Passing: {stats.TotalCounters.Passing} ({CalculatePercentage(stats.TotalCounters.Passing, totalTests):F2}%)");
        writer.WriteLine($"# Failing: {stats.TotalCounters.Failing} ({CalculatePercentage(stats.TotalCounters.Failing, totalTests):F2}%)");
        writer.WriteLine("#");
        writer.WriteLine("# Per-opcode statistics:");

        foreach (var kvp in stats.OpcodeStats.OrderBy(x => x.Key)) {
            string opcode = kvp.Key;
            TestCounters counters = kvp.Value;
            int total = counters.Total;
            writer.WriteLine($"# {opcode}: {counters.Passing}/{total} passing ({CalculatePercentage(counters.Passing, total):F2}%), " +
                           $"{counters.Failing}/{total} failing ({CalculatePercentage(counters.Failing, total):F2}%)");
        }

        writer.WriteLine("#");
        writer.WriteLine("# Failing test hashes:");
        foreach (string hash in stats.FailingTestHashes) {
            if (hash == "aa20aeb777b083eb0bd9e87bbde3ee8c9e4d2b3e") {
                writer.WriteLine("# found it");
            }
            writer.WriteLine(hash);
        }
    }

    /// <summary>
    /// Calculates the percentage of a value relative to a total.
    /// </summary>
    /// <param name="value">The value to calculate percentage for</param>
    /// <param name="total">The total to calculate percentage against</param>
    /// <returns>The percentage, or 0 if total is 0</returns>
    private static double CalculatePercentage(int value, int total) {
        return total > 0 ? (value * 100.0 / total) : 0;
    }

    /// <summary>
    /// Gets the path for a revocation list file based on the configured write base path.
    /// </summary>
    /// <param name="cpuModelString">The CPU model string (e.g., "80386")</param>
    /// <param name="range">The opcode range (e.g., "00-65")</param>
    /// <returns>The full path to the revocation list file</returns>
    private string GetRevocationListPath(string cpuModelString, string range) {
        string sanitizedRange = range.Replace(".", "_");
        string fileName = $"revocation_list_{cpuModelString}_{sanitizedRange}.txt";
        return Path.Combine(_writeBasePath, fileName);
    }
}
