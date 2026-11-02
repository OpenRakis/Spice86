namespace Spice86.Tests.AsmFixtures;

/// <summary>
/// One catalogue scenario reduced to the stable metadata the golden gates work with: its identity, its
/// variant, the keys that address its goldens and the program it runs. An entry is built by preparing
/// one run of the scenario, so every key is the key the runners themselves compute.
/// </summary>
internal sealed class AsmScenarioInventoryEntry {
    /// <summary>Creates an inventory entry.</summary>
    /// <param name="scenario">Catalogue scenario the entry describes.</param>
    /// <param name="identity">Stable scenario identity, independent of host paths.</param>
    /// <param name="variantName">Logical variant, empty for the default variant.</param>
    /// <param name="executionKey">Key of one execution of this scenario and variant.</param>
    /// <param name="machineGoldenKey">Key of the listing and CFG goldens of this scenario and variant.</param>
    /// <param name="generatedSourceKey">Key of the generated source shared by the variants of this configuration.</param>
    /// <param name="primaryProgramName">File name stem of the program the scenario runs.</param>
    public AsmScenarioInventoryEntry(AsmScenario scenario, string identity, string variantName,
        string executionKey, string machineGoldenKey, string generatedSourceKey, string primaryProgramName) {
        Scenario = scenario;
        Identity = identity;
        VariantName = variantName;
        ExecutionKey = executionKey;
        MachineGoldenKey = machineGoldenKey;
        GeneratedSourceKey = generatedSourceKey;
        PrimaryProgramName = primaryProgramName;
    }

    /// <summary>Catalogue scenario this entry describes.</summary>
    public AsmScenario Scenario { get; }

    /// <summary>Stable scenario identity, independent of host paths.</summary>
    public string Identity { get; }

    /// <summary>Logical input or configuration variant, empty for the default variant.</summary>
    public string VariantName { get; }

    /// <summary>Key of one execution of this scenario and variant: the generated-source key plus the
    /// variant component that tells two executions of one generated program apart.</summary>
    public string ExecutionKey { get; }

    /// <summary>Key of the listing and CFG goldens of this scenario and variant.</summary>
    public string MachineGoldenKey { get; }

    /// <summary>Key of the generated-source golden every variant of this configuration shares.</summary>
    public string GeneratedSourceKey { get; }

    /// <summary>File name stem of the program the scenario runs: the resource the scenario owns as its own
    /// primary program instead of borrowing it from another scenario.</summary>
    public string PrimaryProgramName { get; }
}
