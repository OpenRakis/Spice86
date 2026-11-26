namespace Spice86.Tests.AsmFixtures;

/// <summary>Stamps host files a test creates with a fixed timestamp, so the DOS-visible file date and
/// time that reach compared RAM are the same in every run of a scenario.</summary>
internal static class DosTestFiles {
    /// <summary>The fixed DOS-epoch timestamp written to test files.</summary>
    private static readonly DateTime FixedTimeUtc = new(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Sets the creation and write times of a test-created host file to the fixed timestamp.</summary>
    /// <param name="path">Host path of the file to stamp.</param>
    public static void SetFixedTimes(string path) {
        File.SetCreationTimeUtc(path, FixedTimeUtc);
        File.SetLastWriteTimeUtc(path, FixedTimeUtc);
    }

    /// <summary>Sets the creation and write times of a test-created host directory to the fixed
    /// timestamp.</summary>
    /// <param name="path">Host path of the directory to stamp.</param>
    public static void SetFixedDirectoryTimes(string path) {
        Directory.SetCreationTimeUtc(path, FixedTimeUtc);
        Directory.SetLastWriteTimeUtc(path, FixedTimeUtc);
    }
}
