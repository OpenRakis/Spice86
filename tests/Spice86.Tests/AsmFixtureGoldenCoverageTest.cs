namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Exact-set gate over the ASM goldens. The two key spaces come from the catalogue: every scenario and
/// variant execution needs its listing and CFG goldens under its machine-golden key, every generated
/// case needs its generated C# under its generated-source key, and no committed golden may be an
/// orphan. The expected sets are derived from <see cref="GeneratedScenarioCatalog"/>, so registering a
/// scenario and regenerating its goldens updates this gate without editing it.
/// </summary>
public sealed class AsmFixtureGoldenCoverageTest {
    private static readonly IReadOnlyList<AsmScenarioInventoryEntry> Catalogue = GeneratedScenarioCatalog.Catalogue;
    private static readonly IReadOnlyList<GeneratedScenarioCase> Cases = GeneratedScenarioCatalog.All;
    private static readonly IReadOnlyCollection<string> MachineGoldenKeys =
        [.. Catalogue.Select(entry => entry.MachineGoldenKey).Distinct().Order(StringComparer.Ordinal)];
    private static readonly IReadOnlyCollection<string> GeneratedSourceKeys =
        [.. Catalogue.Select(entry => entry.GeneratedSourceKey).Distinct().Order(StringComparer.Ordinal)];
    private static readonly string GoldenRoot = Path.Join(MachineGoldens.SourceResourcesDirectory(), "cpuTests", "res");

    /// <summary>Every scenario and variant gets one execution key and one machine-golden key, and the
    /// only generated-source keys several executions may share are the variants of one scenario.</summary>
    [Fact]
    public void EveryCatalogueScenarioHasUniqueKeysAndIntentionalVariantSharing() {
        // Act
        IReadOnlyList<string> problems = GoldenCoverageGate.ValidateKeySpaces(Catalogue);

        // Assert
        problems.Should().BeEmpty(
            "execution keys and machine-golden keys must be unique, and a generated-source key may only be shared by the variants of one scenario configuration");
    }

    /// <summary>The generated case catalogue covers exactly the catalogue generated-source keys, every
    /// execution of the catalogue, and holds one valid discovery member and at least one execution
    /// member per case.</summary>
    [Fact]
    public void EveryCatalogueScenarioIsRegisteredAsAGeneratedCase() {
        // Act
        IReadOnlyList<string> problems = GoldenCoverageGate.ValidateRegistration(Catalogue, Cases);

        // Assert
        problems.Should().BeEmpty(
            "every catalogue scenario and variant must be registered by exactly one generated case, and every generated-source key must have its case");
    }

    /// <summary>Each golden directory holds exactly the files its catalogue keys name: every missing file
    /// and every orphan file is reported together.</summary>
    [Fact]
    public void EveryGoldenDirectoryHoldsExactlyTheCatalogueGoldens() {
        // Act
        List<string> problems = new();
        AppendDirectoryProblems(problems, "DumpedListing", ".txt", MachineGoldenKeys);
        AppendDirectoryProblems(problems, "DumpedCfgBlocks", ".json", MachineGoldenKeys);
        AppendDirectoryProblems(problems, "DumpedCfgPartitions", ".json", MachineGoldenKeys);
        AppendDirectoryProblems(problems, "GeneratedCode", ".cs.txt", GeneratedSourceKeys);

        // Assert
        problems.Should().BeEmpty(
            "every committed golden belongs to a catalogue key and every catalogue key has its goldens");
    }

    /// <summary>The two file-set diagnostics name opposite sides of the set difference: a missing file is a
    /// catalogue key without its committed golden, an orphan file is a committed golden without its
    /// catalogue key. Neither message may be reported for the other side.</summary>
    [Fact]
    public void MissingAndOrphanFileProblemsNameOppositeSidesOfTheSetDifference() {
        // Arrange: one expected file the directory lacks and one file no key expects
        GoldenFileSetDiff diff = GoldenCoverageGate.CompareFileSet(["alpha.txt"], ["orphan.txt"]);
        List<string> problems = new();

        // Act
        AppendFileSetProblems(problems, "DumpedListing", diff);

        // Assert
        problems.Should().HaveCount(2);
        problems[0].Should().Contain("DumpedListing/alpha.txt")
            .And.Contain("this catalogue key has no committed golden file")
            .And.NotContain("belongs to no catalogue key");
        problems[1].Should().Contain("DumpedListing/orphan.txt")
            .And.Contain("this committed golden belongs to no catalogue key")
            .And.NotContain("has no committed golden file");
    }

    /// <summary>Every resource a catalogue scenario depends on exists in the committed resources, so a
    /// renamed or deleted child program, overlay or data file fails here instead of at run time.</summary>
    [Fact]
    public void EveryScenarioResourceDependencyExistsInTheSourceTree() {
        // Act
        List<string> missing = new();
        foreach (AsmScenarioInventoryEntry entry in Catalogue) {
            foreach (string dependency in entry.Scenario.ResourceDependencies) {
                string path = Path.Join(MachineGoldens.SourceResourcesDirectory(), dependency);
                if (!File.Exists(path)) {
                    missing.Add($"'{dependency}', needed by the scenario '{entry.Identity}', does not exist");
                }
            }
        }

        // Assert
        missing.Should().BeEmpty(
            "child programs, overlays, includes and dummy data stay committed resources of the scenario that loads them");
    }

    /// <summary>A resource file whose name is a scenario identity is never registered standalone by that
    /// scenario while another scenario loads it, unless the named scenario runs the file as its own
    /// primary program: child programs, overlays, includes and dummy data stay dependencies of the
    /// scenario that owns them.</summary>
    [Fact]
    public void AResourceProgramNamedByAScenarioIsDeclaredByThatScenario() {
        // Act
        IReadOnlyList<string> problems = GoldenCoverageGate.ValidateResourceDependencies(Catalogue);

        // Assert
        problems.Should().BeEmpty(
            "child programs, overlays, includes and dummy data run under their owning scenario and are not standalone registrations");
    }

    /// <summary>A scenario that runs a program another scenario loads keeps that program as its own
    /// primary resource, so the resident program of a batch or EXEC scenario may exist as its own
    /// catalogue scenario without either being a second standalone registration.</summary>
    [Fact]
    public void AScenarioThatRunsAResourceAnotherScenarioLoadsIsNotReported() {
        // Arrange: a batch-like scenario that loads a resident program, and the standalone scenario that
        // runs that very program as its own primary resource
        IReadOnlyList<AsmScenarioInventoryEntry> catalogue = [
            EntryRunning("tsr_batch_continues", "START.BAT", "", ["DosTsrTests/tsr_basic.com"]),
            EntryRunning("tsr_basic", "tsr_basic.com", "", ["DosTsrTests/tsr_basic.com"])
        ];

        // Act
        IReadOnlyList<string> problems = GoldenCoverageGate.ValidateResourceDependencies(catalogue);

        // Assert
        problems.Should().BeEmpty(
            "a scenario may own its own primary program even when another scenario loads that program");
    }

    /// <summary>A scenario whose run renames the program it starts still owns the resource that program is,
    /// so a child program run under its own identity with its own goldens is not a second standalone
    /// registration of the resource another scenario loads.</summary>
    [Fact]
    public void AScenarioThatRunsARenamedCopyOfAResourceAnotherScenarioLoadsIsNotReported() {
        // Arrange: a parent scenario that loads a child program, and the child's own scenario, whose run
        // starts the renamed copy TEST.COM and states child.com as the resource it runs
        IReadOnlyList<AsmScenarioInventoryEntry> catalogue = [
            EntryRunning("exec_parent", "master", "", ["DosExecTests/child.com"]),
            EntryRunning("child", "TEST.COM", "child.com", ["DosExecTests/child.com"])
        ];

        // Act
        IReadOnlyList<string> problems = GoldenCoverageGate.ValidateResourceDependencies(catalogue);

        // Assert
        problems.Should().BeEmpty(
            "a scenario may own the primary resource another scenario loads when it runs that resource itself");
    }

    /// <summary>Adds one problem per missing and per orphan file of one golden directory.</summary>
    /// <param name="problems">Problem list to append to.</param>
    /// <param name="directoryName">Golden directory under <c>Resources/cpuTests/res</c>.</param>
    /// <param name="fileExtension">Extension of the golden files of that directory.</param>
    /// <param name="keys">Keys whose files the directory must hold.</param>
    private static void AppendDirectoryProblems(List<string> problems, string directoryName, string fileExtension,
        IReadOnlyCollection<string> keys) {
        string directory = Path.Join(GoldenRoot, directoryName);
        if (!Directory.Exists(directory)) {
            problems.Add($"{directoryName}: the golden directory does not exist");
            return;
        }
        string[] actual = Directory.GetFiles(directory).Select(Path.GetFileName).OfType<string>().ToArray();
        IReadOnlyCollection<string> expected = [.. keys.Select(key => key + fileExtension)];
        GoldenFileSetDiff diff = GoldenCoverageGate.CompareFileSet(expected, actual);
        AppendFileSetProblems(problems, directoryName, diff);
    }

    /// <summary>Adds one problem per missing and per orphan file of one golden directory. A missing file is
    /// a catalogue key whose committed golden does not exist, an orphan file is a committed golden no
    /// catalogue key names, so the two messages never describe the other side of the set difference.</summary>
    /// <param name="problems">Problem list to append to.</param>
    /// <param name="directoryName">Golden directory under <c>Resources/cpuTests/res</c>.</param>
    /// <param name="diff">Difference between the files the catalogue needs and the files held.</param>
    private static void AppendFileSetProblems(List<string> problems, string directoryName, GoldenFileSetDiff diff) {
        foreach (string missing in diff.Missing) {
            problems.Add($"{directoryName}/{missing}: this catalogue key has no committed golden file");
        }
        foreach (string orphan in diff.Orphan) {
            problems.Add($"{directoryName}/{orphan}: this committed golden belongs to no catalogue key");
        }
    }

    /// <summary>An inventory entry of a scenario that runs one program under its own identity, so identity,
    /// keys and primary program all come from the one run the inventory prepares.</summary>
    /// <param name="identity">Stable identity of the scenario.</param>
    /// <param name="runtimeProgramName">File name of the program the run starts.</param>
    /// <param name="primaryResourceFileName">Resource file that program is, empty when the run does not
    /// rename it.</param>
    /// <param name="resourceDependencies">Resource files the scenario needs, relative to the resources root.</param>
    /// <returns>The entry of that scenario.</returns>
    private static AsmScenarioInventoryEntry EntryRunning(string identity, string runtimeProgramName,
        string primaryResourceFileName, IReadOnlyList<string> resourceDependencies) {
        AsmFixture fixture = AsmFixtureCatalog.JmpMov();
        AsmRunSettings settings = fixture.Settings with { ScenarioName = identity, BinName = runtimeProgramName };
        AsmScenario scenario = new AsmScenario(identity,
            _ => new AsmFixture(settings, fixture.ExpectedMemory, fixture.AssertResult), resourceDependencies) {
            PrimaryResourceFileName = primaryResourceFileName
        };
        return AsmScenarioInventory.EntryOf(scenario);
    }
}
