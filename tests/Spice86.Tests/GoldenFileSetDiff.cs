namespace Spice86.Tests;

/// <summary>Difference between the golden files a catalogue needs and the files one golden directory
/// actually holds: the missing files no run can compare and the orphan files no catalogue key names.</summary>
internal sealed class GoldenFileSetDiff {
    /// <summary>Creates a diff.</summary>
    /// <param name="missing">Expected file names the directory does not hold.</param>
    /// <param name="orphan">File names the directory holds that no catalogue key expects.</param>
    public GoldenFileSetDiff(IReadOnlyList<string> missing, IReadOnlyList<string> orphan) {
        Missing = missing;
        Orphan = orphan;
    }

    /// <summary>Expected file names the directory does not hold.</summary>
    public IReadOnlyList<string> Missing { get; }

    /// <summary>File names the directory holds that no catalogue key expects.</summary>
    public IReadOnlyList<string> Orphan { get; }

    /// <summary>Whether the directory holds exactly the expected files.</summary>
    public bool IsEmpty => Missing.Count == 0 && Orphan.Count == 0;
}
