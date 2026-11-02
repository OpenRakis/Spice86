namespace Spice86.Tests.AsmFixtures;

using Spice86.Tests;

/// <summary>
/// Inventory of the two golden key spaces over <see cref="AsmScenarioCatalog.All"/>: one prepared run
/// per catalogue entry yields the scenario identity, the variant, the execution key, the machine-golden
/// key and the generated-source key of that entry, and the run is disposed again. The inventory is the
/// single source of the expected golden file sets, so it grows with the catalogue.
/// </summary>
internal static class AsmScenarioInventory {
    /// <summary>Prepares one run of every catalogue scenario and returns the inventory entries.</summary>
    public static IReadOnlyList<AsmScenarioInventoryEntry> Catalogue() {
        List<AsmScenarioInventoryEntry> entries = new();
        foreach (AsmScenario scenario in AsmScenarioCatalog.All) {
            entries.Add(EntryOf(scenario));
        }
        return entries;
    }

    /// <summary>Prepares one run of the scenario and derives its inventory entry from that run's settings.</summary>
    /// <param name="scenario">Catalogue scenario to derive the entry for.</param>
    /// <returns>The inventory entry, with the scope of the derived run disposed again.</returns>
    public static AsmScenarioInventoryEntry EntryOf(AsmScenario scenario) {
        using AsmRunScope scope = scenario.Prepare();
        AsmRunSettings settings = scope.Fixture.Settings;
        string generatedSourceKey = GeneratedCodeMachineTestRunner.GoldenKey(settings);
        return new AsmScenarioInventoryEntry(
            scenario,
            settings.ScenarioIdentity,
            settings.VariantName,
            ExecutionKey(generatedSourceKey, settings.VariantName),
            MachineGoldens.GoldenKey(scope.Fixture),
            generatedSourceKey,
            PrimaryProgramName(scenario, settings.BinName));
    }

    /// <summary>File name stem of the program the scenario runs: the resource the scenario states when its
    /// run renames that program, the started program otherwise.</summary>
    /// <param name="scenario">Scenario whose program is derived.</param>
    /// <param name="binName">Program file the run starts.</param>
    /// <returns>File name stem of the program the scenario runs.</returns>
    private static string PrimaryProgramName(AsmScenario scenario, string binName) {
        string fileName = scenario.PrimaryResourceFileName.Length > 0 ? scenario.PrimaryResourceFileName : binName;
        return Path.GetFileNameWithoutExtension(fileName);
    }

    /// <summary>Key of one scenario and variant execution: the generated-source key plus the variant
    /// component that unambiguously tells two executions of the same generated program apart.</summary>
    /// <param name="generatedSourceKey">Generated-source key of the execution.</param>
    /// <param name="variantName">Variant of the scenario, empty for the default variant.</param>
    /// <returns>The execution key.</returns>
    public static string ExecutionKey(string generatedSourceKey, string variantName) {
        return variantName.Length == 0 ? generatedSourceKey : generatedSourceKey + "." + variantName;
    }
}
