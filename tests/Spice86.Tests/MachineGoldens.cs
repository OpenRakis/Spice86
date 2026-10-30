namespace Spice86.Tests;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Core.Emulator.VM;

using Spice86.Tests.AsmFixtures;
using Spice86.Tests.Utility;

using System.Runtime.CompilerServices;

using Xunit;

/// <summary>
/// Listing and CFG golden comparisons shared by every registered scenario: <c>MachineTest</c> and any
/// other entry point compare through these helpers, so there is one route to the machine goldens. Both
/// the writers and the comparisons address the committed golden in the source tree, never a build copy.
/// </summary>
internal static class MachineGoldens {
    private static readonly ListingExtractor Dumper = new(new(AsmRenderingConfig.CreateSpice86Style()));

    /// <summary>
    /// Builds the golden-file key of a fixture: the stable scenario identity plus the suffixes of its
    /// non-default configuration. The identity never contains a host path, so a scenario prepared in a
    /// temporary directory keeps the key of its logical name.
    /// </summary>
    /// <param name="fixture">Fixture to build the key for.</param>
    /// <returns>The golden-file key.</returns>
    public static string GoldenKey(AsmFixture fixture) {
        string key = fixture.Settings.ScenarioIdentity;
        if (!fixture.Settings.EnableSpeculativeCfgExploration) {
            key += ".nospec";
        }
        if (fixture.Settings.VariantName.Length > 0) {
            key += "." + fixture.Settings.VariantName;
        }
        return key + fixture.Settings.HandlerSegmentGoldenSuffix;
    }

    /// <summary>Compares the finished machine's assembly listing with the committed golden.</summary>
    /// <param name="goldenKey">Golden-file key of the run.</param>
    /// <param name="machine">Machine of the finished run.</param>
    public static void CompareListingWithExpected(string goldenKey, Machine machine) {
        List<string> actualLines = Dumper.ToAssemblyListing(machine.CfgCpu);
        // WriteExpectedListing(goldenKey, actualLines);
        List<string> expectedLines = GetExpectedListing(goldenKey);
        Assert.Equal(expectedLines, actualLines);
    }

    /// <summary>Compares the finished machine's CFG blocks and partitions with the committed goldens.</summary>
    /// <param name="goldenKey">Golden-file key of the run.</param>
    /// <param name="machine">Machine of the finished run.</param>
    public static void CompareCfgBlocksJsonWithExpected(string goldenKey, Machine machine) {
        ExecutionContextManager contextManager = machine.CfgCpu.ExecutionContextManager;
        string actualBlocksJson = CfgBlocksTestJson.SerializeBlocks(contextManager);
        string actualPartitionsJson = CfgBlocksTestJson.SerializePartitions(contextManager);
        // WriteExpectedCfgJson(goldenKey, actualBlocksJson, actualPartitionsJson);
        Assert.Equal(GetExpectedCfgJson(goldenKey, "DumpedCfgBlocks"), actualBlocksJson);
        Assert.Equal(GetExpectedCfgJson(goldenKey, "DumpedCfgPartitions"), actualPartitionsJson);
    }

    private static List<string> GetExpectedListing(string goldenKey) {
        return File.ReadAllLines(GetGoldenPath("DumpedListing", goldenKey + ".txt")).ToList();
    }

    private static void WriteExpectedListing(string goldenKey, List<string> expected) {
        // Write directly to the source tree so the golden file is committed alongside the code.
        // Reading through the same path keeps write and comparison on one golden file: a golden written
        // during a regeneration pass must be readable again in that same pass.
        File.WriteAllLines(GetGoldenPath("DumpedListing", goldenKey + ".txt"), expected);
    }

    private static string GetExpectedCfgJson(string goldenKey, string subDirectory) {
        return File.ReadAllText(GetGoldenPath(subDirectory, goldenKey + ".json"));
    }

    private static void WriteExpectedCfgJson(string goldenKey, string blocksJson, string partitionsJson) {
        WriteCfgResource(goldenKey, "DumpedCfgBlocks", blocksJson);
        WriteCfgResource(goldenKey, "DumpedCfgPartitions", partitionsJson);
    }

    private static void WriteCfgResource(string goldenKey, string subDirectory, string json) {
        string resPath = GetGoldenPath(subDirectory, goldenKey + ".json");
        Directory.CreateDirectory(GetDirectoryName(resPath));
        File.WriteAllText(resPath, json);
    }

    /// <summary>Path of a committed golden in the source tree, the file both the writers and the
    /// comparisons use.</summary>
    /// <param name="subDirectory">Golden directory under <c>Resources/cpuTests/res</c>.</param>
    /// <param name="fileName">File name of the golden.</param>
    /// <returns>Absolute path of that golden.</returns>
    private static string GetGoldenPath(string subDirectory, string fileName) {
        return Path.Join(SourceResourcesDirectory(), "cpuTests", "res", subDirectory, fileName);
    }

    /// <summary>Committed ASM test resources in the source tree: the directory the golden writers, the
    /// golden comparisons and the resource dependencies of the catalogue all address.</summary>
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
