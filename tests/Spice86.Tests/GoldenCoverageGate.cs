namespace Spice86.Tests;

using Spice86.Tests.AsmFixtures;

/// <summary>
/// Pure checks behind the ASM golden gate. Every method takes its data in memory and returns problem
/// descriptions instead of asserting, so the gate itself and its negative tests share one
/// implementation: the coverage test asserts an empty problem list, the negative tests assert the
/// problem their broken data must produce.
/// </summary>
internal static class GoldenCoverageGate {
    /// <summary>Problems of the two key spaces: every scenario and variant execution needs one unique
    /// execution key and one unique machine-golden key, and a generated-source key may only be shared
    /// by the variants of one scenario configuration, with exactly one empty-variant discovery member.</summary>
    /// <param name="catalogue">Inventory entries to validate.</param>
    /// <returns>Problem descriptions, empty when the key spaces are sound.</returns>
    public static IReadOnlyList<string> ValidateKeySpaces(IReadOnlyList<AsmScenarioInventoryEntry> catalogue) {
        List<string> problems = new();
        AppendDuplicateKeyProblems(problems, "execution", entry => entry.ExecutionKey, catalogue);
        AppendDuplicateKeyProblems(problems, "machine-golden", entry => entry.MachineGoldenKey, catalogue);
        AppendSharedGeneratedSourceProblems(problems, catalogue);
        return problems;
    }

    /// <summary>Problems of the generated registration: every discovery and execution member must be a
    /// catalogue entry that matches it on its stable metadata, the discovery member must be one of the
    /// case executions, every execution key must be registered exactly once, the cases must cover exactly
    /// the catalogue generated-source keys, and their execution keys must be exactly the catalogue
    /// execution keys.</summary>
    /// <param name="catalogue">Inventory entries the cases must cover.</param>
    /// <param name="cases">Generated cases to validate against the catalogue.</param>
    /// <returns>Problem descriptions, empty when the registration is complete.</returns>
    public static IReadOnlyList<string> ValidateRegistration(
        IReadOnlyList<AsmScenarioInventoryEntry> catalogue, IReadOnlyList<GeneratedScenarioCase> cases) {
        List<string> problems = new();
        IReadOnlyDictionary<string, AsmScenarioInventoryEntry> catalogueByExecutionKey =
            BuildCatalogueByExecutionKey(catalogue);
        HashSet<string> caseKeys = new(StringComparer.Ordinal);
        Dictionary<string, int> registrations = new(StringComparer.Ordinal);
        foreach (GeneratedScenarioCase scenarioCase in cases) {
            AppendCaseKeyProblem(problems, caseKeys, scenarioCase);
            AppendCaseMemberProblems(problems, scenarioCase, catalogueByExecutionKey);
            foreach (string executionKey in scenarioCase.ExecutionKeys) {
                registrations[executionKey] = registrations.GetValueOrDefault(executionKey) + 1;
            }
        }
        AppendRepeatedRegistrationProblems(problems, registrations);
        HashSet<string> catalogueKeys = new(catalogue.Select(entry => entry.GeneratedSourceKey), StringComparer.Ordinal);
        HashSet<string> catalogueExecutionKeys = new(catalogue.Select(entry => entry.ExecutionKey), StringComparer.Ordinal);
        HashSet<string> registeredExecutionKeys = new(registrations.Keys, StringComparer.Ordinal);
        AppendKeySetProblems(problems, "generated-source key", catalogueKeys, caseKeys,
            "has no registered generated case");
        AppendKeySetProblems(problems, "generated case", caseKeys, catalogueKeys,
            "is not a catalogue generated-source key");
        AppendKeySetProblems(problems, "execution key", catalogueExecutionKeys, registeredExecutionKeys,
            "is not registered by any generated case");
        AppendKeySetProblems(problems, "registered execution key", registeredExecutionKeys, catalogueExecutionKeys,
            "is not a catalogue execution key");
        return problems;
    }

    /// <summary>Problems of the resource dependencies: a resource file whose name is a scenario identity
    /// must not be registered standalone by that scenario while another scenario loads it, so a child
    /// program, overlay, include or dummy data file stays a dependency of the scenario that owns it. A
    /// loader declaring the resource its own identity names owns its primary resource, and a named
    /// scenario running that resource as its own primary program owns it too; both are skipped, while
    /// merely declaring the dependency another scenario loads is not enough.</summary>
    /// <param name="catalogue">Inventory entries to validate.</param>
    /// <returns>Problem descriptions, empty when every named resource program is owned by its scenario.</returns>
    public static IReadOnlyList<string> ValidateResourceDependencies(
        IReadOnlyList<AsmScenarioInventoryEntry> catalogue) {
        List<string> problems = new();
        foreach (AsmScenarioInventoryEntry entry in catalogue) {
            foreach (string dependency in entry.Scenario.ResourceDependencies) {
                AppendUnownedNamedResourceProblems(problems, dependency, entry, catalogue);
            }
        }
        return problems;
    }

    /// <summary>Difference between the expected golden file names of one directory and the file names the
    /// directory actually holds, missing and orphan files together.</summary>
    /// <param name="expectedFileNames">File names the catalogue needs.</param>
    /// <param name="actualFileNames">File names the directory holds.</param>
    /// <returns>The missing and the orphan file names.</returns>
    public static GoldenFileSetDiff CompareFileSet(IReadOnlyCollection<string> expectedFileNames,
        IReadOnlyCollection<string> actualFileNames) {
        List<string> missing = [.. Sort(expectedFileNames.Except(actualFileNames))];
        List<string> orphan = [.. Sort(actualFileNames.Except(expectedFileNames))];
        return new GoldenFileSetDiff(missing, orphan);
    }

    private static void AppendDuplicateKeyProblems(List<string> problems, string keySpace,
        Func<AsmScenarioInventoryEntry, string> keyOf, IReadOnlyList<AsmScenarioInventoryEntry> catalogue) {
        foreach (IGrouping<string, AsmScenarioInventoryEntry> group in catalogue.GroupBy(keyOf)
                     .Where(group => group.Count() > 1)) {
            problems.Add(
                $"the {keySpace} key '{group.Key}' is shared by {group.Count()} scenario runs: {Describe(group)}");
        }
    }

    private static void AppendSharedGeneratedSourceProblems(List<string> problems,
        IReadOnlyList<AsmScenarioInventoryEntry> catalogue) {
        foreach (IGrouping<string, AsmScenarioInventoryEntry> group in catalogue.GroupBy(entry => entry.GeneratedSourceKey)) {
            List<AsmScenarioInventoryEntry> members = [.. group];
            if (members.Count < 2) {
                continue;
            }
            AppendSharedIdentityProblem(problems, group.Key, members);
            AppendDiscoveryMemberProblems(problems, group.Key, members);
            AppendRepeatedVariantProblems(problems, group.Key, members);
        }
    }

    private static void AppendSharedIdentityProblem(List<string> problems, string generatedSourceKey,
        IReadOnlyList<AsmScenarioInventoryEntry> members) {
        string[] identities = [.. members.Select(member => member.Identity).Distinct().Order(StringComparer.Ordinal)];
        if (identities.Length > 1) {
            problems.Add(
                $"the generated-source key '{generatedSourceKey}' is shared by the unrelated scenarios {DescribeIdentities(identities)}");
        }
    }

    private static void AppendDiscoveryMemberProblems(List<string> problems, string generatedSourceKey,
        IReadOnlyList<AsmScenarioInventoryEntry> members) {
        int discoveryMembers = members.Count(member => member.VariantName.Length == 0);
        if (discoveryMembers != 1) {
            problems.Add(
                $"the generated-source group '{generatedSourceKey}' has {discoveryMembers} default-variant members;" +
                " a shared generated program needs exactly one discovery scenario with an empty variant");
        }
    }

    private static void AppendRepeatedVariantProblems(List<string> problems, string generatedSourceKey,
        IReadOnlyList<AsmScenarioInventoryEntry> members) {
        foreach (IGrouping<string, AsmScenarioInventoryEntry> variants in members.GroupBy(member => member.VariantName)
                     .Where(variants => variants.Count() > 1)) {
            problems.Add($"the generated-source group '{generatedSourceKey}' repeats the variant '{variants.Key}'");
        }
    }

    private static void AppendCaseKeyProblem(List<string> problems, HashSet<string> caseKeys,
        GeneratedScenarioCase scenarioCase) {
        if (!caseKeys.Add(scenarioCase.GeneratedSourceKey)) {
            problems.Add($"the generated-source key '{scenarioCase.GeneratedSourceKey}' has more than one case");
        }
    }

    private static void AppendCaseMemberProblems(List<string> problems, GeneratedScenarioCase scenarioCase,
        IReadOnlyDictionary<string, AsmScenarioInventoryEntry> catalogueByExecutionKey) {
        string key = scenarioCase.GeneratedSourceKey;
        if (scenarioCase.Discovery.VariantName.Length > 0) {
            problems.Add(
                $"the case '{key}' discovers from the '{scenarioCase.Discovery.VariantName}' variant instead of a default-variant member");
        }
        if (scenarioCase.Discovery.GeneratedSourceKey != key) {
            problems.Add(
                $"the discovery member '{scenarioCase.Discovery.Identity}' of the case '{key}' generates '{scenarioCase.Discovery.GeneratedSourceKey}'");
        }
        AppendDiscoveryProblems(problems, key, scenarioCase, catalogueByExecutionKey);
        if (scenarioCase.Executions.Count == 0) {
            problems.Add($"the case '{key}' has no execution member");
        }
        foreach (AsmScenarioInventoryEntry execution in scenarioCase.Executions) {
            if (execution.GeneratedSourceKey != key) {
                problems.Add(
                    $"the execution member '{execution.Identity}' of the case '{key}' generates '{execution.GeneratedSourceKey}'");
            }
            AppendCatalogueEntryProblems(problems, "execution", key, execution, catalogueByExecutionKey);
        }
    }

    /// <summary>Adds a problem when the discovery member is not a catalogue execution or is missing from
    /// the execution list of its own case.</summary>
    /// <param name="problems">Problem list to append to.</param>
    /// <param name="caseKey">Generated-source key of the case the discovery belongs to.</param>
    /// <param name="scenarioCase">Case whose discovery member is validated.</param>
    /// <param name="catalogueByExecutionKey">Catalogue entries by execution key.</param>
    private static void AppendDiscoveryProblems(List<string> problems, string caseKey,
        GeneratedScenarioCase scenarioCase,
        IReadOnlyDictionary<string, AsmScenarioInventoryEntry> catalogueByExecutionKey) {
        AsmScenarioInventoryEntry discovery = scenarioCase.Discovery;
        AppendCatalogueEntryProblems(problems, "discovery", caseKey, discovery, catalogueByExecutionKey);
        if (!scenarioCase.Executions.Any(execution => execution.ExecutionKey == discovery.ExecutionKey)) {
            problems.Add(
                $"the discovery member '{discovery.Identity}' of the case '{caseKey}' is not one of the case executions");
        }
    }

    /// <summary>Adds a problem when a case member has no catalogue entry for its execution key, or when its
    /// metadata disagrees with the catalogue entry it names.</summary>
    /// <param name="problems">Problem list to append to.</param>
    /// <param name="memberRole">Role of the member in the case, <c>discovery</c> or <c>execution</c>.</param>
    /// <param name="caseKey">Generated-source key of the case the member belongs to.</param>
    /// <param name="member">Case member to validate.</param>
    /// <param name="catalogueByExecutionKey">Catalogue entries by execution key.</param>
    private static void AppendCatalogueEntryProblems(List<string> problems, string memberRole, string caseKey,
        AsmScenarioInventoryEntry member,
        IReadOnlyDictionary<string, AsmScenarioInventoryEntry> catalogueByExecutionKey) {
        if (!catalogueByExecutionKey.TryGetValue(member.ExecutionKey, out AsmScenarioInventoryEntry? catalogueEntry)) {
            problems.Add(
                $"the {memberRole} member '{member.Identity}' of the case '{caseKey}' has the unknown execution key '{member.ExecutionKey}'");
            return;
        }
        AppendMetadataMismatchProblem(problems, memberRole, caseKey, member, catalogueEntry);
    }

    /// <summary>Adds one problem listing every field where a case member disagrees with its catalogue entry,
    /// so a registered member cannot take over the execution key of another entry.</summary>
    /// <param name="problems">Problem list to append to.</param>
    /// <param name="memberRole">Role of the member in the case, <c>discovery</c> or <c>execution</c>.</param>
    /// <param name="caseKey">Generated-source key of the case the member belongs to.</param>
    /// <param name="member">Case member to compare.</param>
    /// <param name="catalogueEntry">Catalogue entry of the same execution key.</param>
    private static void AppendMetadataMismatchProblem(List<string> problems, string memberRole, string caseKey,
        AsmScenarioInventoryEntry member, AsmScenarioInventoryEntry catalogueEntry) {
        List<string> mismatches = new();
        AppendFieldMismatch(mismatches, "identity", member.Identity, catalogueEntry.Identity);
        AppendFieldMismatch(mismatches, "variant", member.VariantName, catalogueEntry.VariantName);
        AppendFieldMismatch(mismatches, "execution key", member.ExecutionKey, catalogueEntry.ExecutionKey);
        AppendFieldMismatch(mismatches, "machine-golden key", member.MachineGoldenKey, catalogueEntry.MachineGoldenKey);
        AppendFieldMismatch(mismatches, "generated-source key", member.GeneratedSourceKey,
            catalogueEntry.GeneratedSourceKey);
        if (mismatches.Count == 0) {
            return;
        }
        problems.Add(
            $"the {memberRole} member of the case '{caseKey}' does not match the catalogue entry of the execution key '{catalogueEntry.ExecutionKey}': {string.Join(", ", mismatches)}");
    }

    private static void AppendFieldMismatch(List<string> mismatches, string field, string actual, string expected) {
        if (actual != expected) {
            mismatches.Add($"{field} '{actual}' instead of '{expected}'");
        }
    }

    /// <summary>Adds a problem for every execution key more than one case member registers.</summary>
    /// <param name="problems">Problem list to append to.</param>
    /// <param name="registrations">Occurrence count of every registered execution key.</param>
    private static void AppendRepeatedRegistrationProblems(List<string> problems,
        IReadOnlyDictionary<string, int> registrations) {
        foreach (KeyValuePair<string, int> registration in registrations
                     .Where(registration => registration.Value > 1)) {
            problems.Add(
                $"the execution key '{registration.Key}' is registered {Times(registration.Value)} instead of exactly once");
        }
    }

    /// <summary>Builds the catalogue entries by execution key. A duplicated catalogue execution key keeps
    /// the first entry instead of throwing, because <see cref="ValidateKeySpaces"/> reports that defect.</summary>
    /// <param name="catalogue">Inventory entries to look up.</param>
    /// <returns>Catalogue entries indexed by execution key.</returns>
    private static IReadOnlyDictionary<string, AsmScenarioInventoryEntry> BuildCatalogueByExecutionKey(
        IReadOnlyList<AsmScenarioInventoryEntry> catalogue) {
        Dictionary<string, AsmScenarioInventoryEntry> entries = new(StringComparer.Ordinal);
        foreach (AsmScenarioInventoryEntry entry in catalogue) {
            entries.TryAdd(entry.ExecutionKey, entry);
        }
        return entries;
    }

    private static string Times(int count) {
        return count == 2 ? "twice" : $"{count} times";
    }

    private static void AppendKeySetProblems(List<string> problems, string keySpace,
        IReadOnlySet<string> expected, IReadOnlySet<string> actual, string problem) {
        foreach (string key in Sort(expected.Except(actual))) {
            problems.Add($"the {keySpace} '{key}' {problem}");
        }
    }

    private static void AppendUnownedNamedResourceProblems(List<string> problems, string dependency,
        AsmScenarioInventoryEntry loader, IReadOnlyList<AsmScenarioInventoryEntry> catalogue) {
        string fileName = Path.GetFileNameWithoutExtension(dependency);
        foreach (AsmScenarioInventoryEntry entry in catalogue) {
            if (entry.Identity != fileName || entry.Identity == loader.Identity) {
                continue;
            }
            if (entry.PrimaryProgramName == fileName) {
                continue;
            }
            problems.Add(
                $"'{dependency}', loaded by the scenario '{loader.Identity}', is named by the scenario '{entry.Identity}' which registers it standalone");
        }
    }

    private static IEnumerable<string> Sort(IEnumerable<string> names) {
        return names.OrderBy(name => name, StringComparer.Ordinal);
    }

    private static string Describe(IEnumerable<AsmScenarioInventoryEntry> entries) {
        return string.Join(", ", entries.Select(entry => $"'{entry.Identity}' variant '{entry.VariantName}'"));
    }

    private static string DescribeIdentities(IEnumerable<string> identities) {
        return string.Join(", ", identities.Select(identity => $"'{identity}'"));
    }
}
