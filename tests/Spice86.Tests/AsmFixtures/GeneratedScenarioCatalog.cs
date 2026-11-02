namespace Spice86.Tests.AsmFixtures;

/// <summary>
/// Generated scenario-case catalogue derived from <see cref="AsmScenarioCatalog.All"/> and grouped by
/// generated-source key: one case per generated-source golden, the empty-variant member of a group as
/// its discovery scenario and every member of the group as a generated execution. The catalogue is the
/// registration source of truth for the normal generated theory, so a new catalogue scenario becomes a
/// generated case without editing a test.
/// </summary>
internal static class GeneratedScenarioCatalog {
    private static readonly IReadOnlyList<AsmScenarioInventoryEntry> CatalogueEntries = AsmScenarioInventory.Catalogue();
    private static readonly IReadOnlyList<GeneratedScenarioCase> Cases = BuildCases(CatalogueEntries);

    /// <summary>Inventory of every catalogue scenario the cases were built from.</summary>
    public static IReadOnlyList<AsmScenarioInventoryEntry> Catalogue => CatalogueEntries;

    /// <summary>One case per generated-source key of the catalogue.</summary>
    public static IReadOnlyList<GeneratedScenarioCase> All => Cases;

    /// <summary>Generated-source keys of every case, as xUnit theory data.</summary>
    public static IEnumerable<object[]> CaseKeys =>
        Cases.Select(scenarioCase => new object[] { scenarioCase.GeneratedSourceKey });

    /// <summary>Finds a case by its generated-source key.</summary>
    /// <param name="generatedSourceKey">Generated-source key of the case.</param>
    /// <returns>The case with that key.</returns>
    /// <exception cref="InvalidOperationException">No case has that key.</exception>
    public static GeneratedScenarioCase Case(string generatedSourceKey) {
        return Cases.FirstOrDefault(scenarioCase => scenarioCase.GeneratedSourceKey == generatedSourceKey)
            ?? throw new InvalidOperationException($"No generated scenario case '{generatedSourceKey}'.");
    }

    /// <summary>Groups the inventory entries into one case per generated-source key. Invalid groups are
    /// still reported as a case so that the coverage gate, not this builder, names the defect.</summary>
    /// <param name="catalogue">Inventory entries to group.</param>
    /// <returns>One case per generated-source key, ordered by key.</returns>
    public static IReadOnlyList<GeneratedScenarioCase> BuildCases(
        IReadOnlyList<AsmScenarioInventoryEntry> catalogue) {
        List<GeneratedScenarioCase> cases = new();
        IEnumerable<IGrouping<string, AsmScenarioInventoryEntry>> groups =
            catalogue.GroupBy(entry => entry.GeneratedSourceKey)
                .OrderBy(group => group.Key, StringComparer.Ordinal);
        foreach (IGrouping<string, AsmScenarioInventoryEntry> group in groups) {
            cases.Add(BuildCase(group.Key, group));
        }
        return cases;
    }

    private static GeneratedScenarioCase BuildCase(string generatedSourceKey,
        IEnumerable<AsmScenarioInventoryEntry> members) {
        List<AsmScenarioInventoryEntry> group = [.. members];
        AsmScenarioInventoryEntry discovery =
            group.FirstOrDefault(entry => entry.VariantName.Length == 0) ?? group[0];
        return new GeneratedScenarioCase(generatedSourceKey, discovery, group);
    }
}
