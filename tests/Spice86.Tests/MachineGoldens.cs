namespace Spice86.Tests;

using Spice86.Tests.AsmFixtures;

using System.Runtime.CompilerServices;

/// <summary>
/// Golden-key helpers shared by the metrics, censuses, reports, check lines and the discovery-dump copy:
/// one row name per scenario run.
/// </summary>
internal static class MachineGoldens {
    /// <summary>
    /// Builds the golden-file key of a fixture: the stable scenario identity plus the suffixes of its
    /// non-default configuration. The identity never contains a host path, so a scenario prepared in a
    /// temporary directory keeps the key of its logical name.
    /// </summary>
    /// <param name="fixture">Fixture to build the key for.</param>
    /// <returns>The golden-file key.</returns>
    public static string GoldenKey(AsmFixture fixture) => GoldenKey(fixture.Settings);

    /// <summary>
    /// Builds the golden-file key of a run from its settings.
    /// </summary>
    /// <param name="settings">Run settings to build the key for.</param>
    /// <returns>The golden-file key.</returns>
    public static string GoldenKey(AsmRunSettings settings) {
        string key = settings.ScenarioIdentity;
        if (!settings.EnableSpeculativeCfgExploration) {
            key += ".nospec";
        }
        if (settings.VariantName.Length > 0) {
            key += "." + settings.VariantName;
        }
        if (!settings.HltStopsEmulation) {
            key += ".hltwait";
        }
        key += settings.GenerationPolicy.GoldenSuffix;
        return key + settings.HandlerSegmentGoldenSuffix;
    }

    /// <summary>Committed ASM test resources in the source tree: the directory the resource dependencies
    /// of the catalogue address.</summary>
    /// <returns>Absolute path of <c>tests/Spice86.Tests/Resources</c>.</returns>
    public static string SourceResourcesDirectory() {
        string sourceDir = GetDirectoryName(GetSourceFilePath());
        return Path.Join(sourceDir, "Resources");
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;

    private static string GetDirectoryName(string path) {
        return Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"No directory for path: {path}");
    }
}
