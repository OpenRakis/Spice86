namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Negative tests of <see cref="GoldenCoverageGate"/>, all on in-memory data: a broken registration, a
/// broken variant group and a broken golden file set must be named by the same validators the coverage
/// gate uses on the real catalogue. No committed golden is deleted, renamed or rewritten here.
/// </summary>
public sealed class GoldenCoverageGateNegativeTest {
    /// <summary>A catalogue execution that no generated case registers is reported, so a scenario variant
    /// cannot silently lose its guarded generated execution.</summary>
    [Fact]
    public void ACatalogueExecutionKeyMissingFromTheGeneratedRegistrationIsReported() {
        // Arrange: one generated-source group with a default variant and an unobserved variant, whose
        // registration drops the unobserved execution
        AsmScenarioInventoryEntry observed = Entry("alpha", "", "alpha.spec");
        AsmScenarioInventoryEntry unobserved = Entry("alpha", "unobserved", "alpha.spec");
        IReadOnlyList<AsmScenarioInventoryEntry> catalogue = [observed, unobserved];
        IReadOnlyList<GeneratedScenarioCase> cases = [new("alpha.spec", observed, [observed])];

        // Act
        IReadOnlyList<string> problems = GoldenCoverageGate.ValidateRegistration(catalogue, cases);

        // Assert
        problems.Should().ContainSingle()
            .Which.Should().Contain("alpha.spec.unobserved").And.Contain("not registered by any generated case");
    }

    /// <summary>A generated-source case that no catalogue key produces is reported, so a case cannot
    /// invent a golden that no scenario generates.</summary>
    [Fact]
    public void AGeneratedCaseOfAnUnknownGeneratedSourceKeyIsReported() {
        // Arrange: a case whose generated-source key no catalogue scenario produces
        IReadOnlyList<AsmScenarioInventoryEntry> catalogue = [Entry("alpha", "", "alpha.spec")];
        IReadOnlyList<GeneratedScenarioCase> cases = [new("alpha.spec", catalogue[0], [catalogue[0]]), UnknownCase()];

        // Act
        IReadOnlyList<string> problems = GoldenCoverageGate.ValidateRegistration(catalogue, cases);

        // Assert
        problems.Should().Contain(
            problem => problem.Contains("the generated case 'gamma.spec' is not a catalogue generated-source key"));
        problems.Should().Contain(
            problem => problem.Contains("the registered execution key 'gamma.spec' is not a catalogue execution key"));
    }

    /// <summary>A generated-source case the catalogue produces but the registration omits is reported,
    /// so a whole scenario cannot silently lose its generated pipeline.</summary>
    [Fact]
    public void ACatalogueGeneratedSourceKeyWithoutACaseIsReported() {
        // Arrange: the catalogue generates two programs, only one of which has a case
        IReadOnlyList<AsmScenarioInventoryEntry> catalogue =
            [Entry("alpha", "", "alpha.spec"), Entry("beta", "", "beta.spec")];
        IReadOnlyList<GeneratedScenarioCase> cases = [new("alpha.spec", catalogue[0], [catalogue[0]])];

        // Act
        IReadOnlyList<string> problems = GoldenCoverageGate.ValidateRegistration(catalogue, cases);

        // Assert
        problems.Should().Contain(
            problem => problem.Contains("the generated-source key 'beta.spec' has no registered generated case"));
        problems.Should().Contain(
            problem => problem.Contains("the execution key 'beta.spec' is not registered by any generated case"));
    }

    /// <summary>Two scenario runs that resolve to one execution key are reported.</summary>
    [Fact]
    public void ADuplicateExecutionKeyIsReported() {
        // Arrange: the same scenario and variant registered twice under one generated-source key
        IReadOnlyList<AsmScenarioInventoryEntry> catalogue =
            [Entry("alpha", "", "alpha.spec"), Entry("alpha", "", "alpha.spec")];

        // Act
        IReadOnlyList<string> problems = GoldenCoverageGate.ValidateKeySpaces(catalogue);

        // Assert
        problems.Should().Contain(problem => problem.Contains("the execution key 'alpha.spec' is shared by 2"));
    }

    /// <summary>Two unrelated scenarios that resolve to one generated-source key are reported, so key
    /// sharing stays limited to the variants of one scenario configuration.</summary>
    [Fact]
    public void AnUnrelatedGeneratedSourceKeyCollisionIsReported() {
        // Arrange: two different identities whose runs produce the same generated-source key
        IReadOnlyList<AsmScenarioInventoryEntry> catalogue =
            [Entry("alpha", "", "alpha.spec"), Entry("beta", "unobserved", "alpha.spec")];

        // Act
        IReadOnlyList<string> problems = GoldenCoverageGate.ValidateKeySpaces(catalogue);

        // Assert
        problems.Should().ContainSingle()
            .Which.Should().Contain("alpha.spec").And.Contain("unrelated").And.Contain("'alpha'").And.Contain("'beta'");
    }

    /// <summary>A generated-source group whose members are all variants is reported: a shared generated
    /// program needs exactly one default-variant discovery member, and the case built from it must not
    /// discover from a variant either.</summary>
    [Fact]
    public void AMultiVariantGroupWithoutADefaultVariantDiscoveryMemberIsReported() {
        // Arrange: one generated-source group holding only named variants
        IReadOnlyList<AsmScenarioInventoryEntry> catalogue =
            [Entry("alpha", "observed", "alpha.spec"), Entry("alpha", "unobserved", "alpha.spec")];

        // Act
        IReadOnlyList<string> keySpaceProblems = GoldenCoverageGate.ValidateKeySpaces(catalogue);
        IReadOnlyList<string> registrationProblems =
            GoldenCoverageGate.ValidateRegistration(catalogue, GeneratedScenarioCatalog.BuildCases(catalogue));

        // Assert
        keySpaceProblems.Should().ContainSingle()
            .Which.Should().Contain("alpha.spec").And.Contain("default-variant");
        registrationProblems.Should().ContainSingle()
            .Which.Should().Contain("discovers from the 'observed' variant");
    }

    /// <summary>A case that discovers from an entry no catalogue execution has is reported twice: the
    /// discovery carries an unknown execution key and it is not one of the case executions, so a
    /// discovery member cannot invent an execution the catalogue never prepared.</summary>
    [Fact]
    public void ADiscoveryMemberThatIsNotACatalogueExecutionIsReported() {
        // Arrange: the real catalogue execution is the only execution member, while the case discovers
        // from a default-variant entry whose execution key no catalogue entry has
        AsmScenarioInventoryEntry real = Entry("alpha", "", "alpha.spec");
        AsmScenarioInventoryEntry unknown = new(
            real.Scenario, real.Identity, "", real.ExecutionKey + ".ghost", real.MachineGoldenKey,
            real.GeneratedSourceKey, real.PrimaryProgramName);
        IReadOnlyList<AsmScenarioInventoryEntry> catalogue = [real];
        IReadOnlyList<GeneratedScenarioCase> cases = [new("alpha.spec", unknown, [real])];

        // Act
        IReadOnlyList<string> problems = GoldenCoverageGate.ValidateRegistration(catalogue, cases);

        // Assert
        problems.Should().Contain(problem =>
            problem.Contains("the discovery member 'alpha' of the case 'alpha.spec' has the unknown execution key 'alpha.spec.ghost'"));
        problems.Should().Contain(problem =>
            problem.Contains("the discovery member 'alpha' of the case 'alpha.spec' is not one of the case executions"));
    }

    /// <summary>A discovery member that reuses a catalogue execution key but disagrees with that catalogue
    /// entry on its metadata is reported, so a case cannot discover from an impostor of a real execution.</summary>
    [Fact]
    public void ADiscoveryMemberThatDisagreesWithItsCatalogueEntryIsReported() {
        // Arrange: the discovery reuses the execution key of the real catalogue entry under another identity
        AsmScenarioInventoryEntry real = Entry("alpha", "", "alpha.spec");
        AsmScenarioInventoryEntry impostor = new(
            real.Scenario, "beta", real.VariantName, real.ExecutionKey, real.MachineGoldenKey,
            real.GeneratedSourceKey, real.PrimaryProgramName);
        IReadOnlyList<AsmScenarioInventoryEntry> catalogue = [real];
        IReadOnlyList<GeneratedScenarioCase> cases = [new("alpha.spec", impostor, [real])];

        // Act
        IReadOnlyList<string> problems = GoldenCoverageGate.ValidateRegistration(catalogue, cases);

        // Assert
        problems.Should().ContainSingle()
            .Which.Should().Contain("the discovery member of the case 'alpha.spec' does not match the catalogue entry")
            .And.Contain("identity 'beta' instead of 'alpha'");
    }

    /// <summary>A case that lists the same catalogue execution twice is reported as a double registration,
    /// so one execution cannot run through one generated case more than once.</summary>
    [Fact]
    public void ADuplicatedExecutionMemberIsReported() {
        // Arrange: the only catalogue execution appears twice in the execution list of its own case
        AsmScenarioInventoryEntry real = Entry("alpha", "", "alpha.spec");
        IReadOnlyList<AsmScenarioInventoryEntry> catalogue = [real];
        IReadOnlyList<GeneratedScenarioCase> cases = [new("alpha.spec", real, [real, real])];

        // Act
        IReadOnlyList<string> problems = GoldenCoverageGate.ValidateRegistration(catalogue, cases);

        // Assert
        problems.Should().ContainSingle()
            .Which.Should().Contain("the execution key 'alpha.spec' is registered twice");
    }

    /// <summary>A catalogue execution two different cases register is reported as a double registration,
    /// so one execution cannot run through two generated cases.</summary>
    [Fact]
    public void AnExecutionRegisteredByTwoCasesIsReported() {
        // Arrange: the alpha execution is registered by its own case and again by the beta case
        AsmScenarioInventoryEntry alpha = Entry("alpha", "", "alpha.spec");
        AsmScenarioInventoryEntry beta = Entry("beta", "", "beta.spec");
        IReadOnlyList<AsmScenarioInventoryEntry> catalogue = [alpha, beta];
        IReadOnlyList<GeneratedScenarioCase> cases =
            [new("alpha.spec", alpha, [alpha]), new("beta.spec", beta, [beta, alpha])];

        // Act
        IReadOnlyList<string> problems = GoldenCoverageGate.ValidateRegistration(catalogue, cases);

        // Assert
        problems.Should().Contain(problem =>
            problem.Contains("the execution key 'alpha.spec' is registered twice"));
    }

    /// <summary>A case execution that reuses a catalogue execution key while changing one metadata field is
    /// reported, so a registered execution cannot disagree with the catalogue entry it names.</summary>
    /// <param name="changedField">Field label the impostor entry changes.</param>
    [Theory]
    [InlineData("identity")]
    [InlineData("variant")]
    [InlineData("machine-golden key")]
    [InlineData("generated-source key")]
    public void AnExecutionMemberThatDisagreesWithItsCatalogueEntryIsReported(string changedField) {
        // Arrange: the execution reuses the catalogue execution key with one field changed
        AsmScenarioInventoryEntry real = Entry("alpha", "", "alpha.spec");
        AsmScenarioInventoryEntry impostor = ExecutionWithChangedMetadata(real, changedField);
        IReadOnlyList<AsmScenarioInventoryEntry> catalogue = [real];
        IReadOnlyList<GeneratedScenarioCase> cases = [new("alpha.spec", real, [impostor])];

        // Act
        IReadOnlyList<string> problems = GoldenCoverageGate.ValidateRegistration(catalogue, cases);

        // Assert
        problems.Should().Contain(problem =>
            problem.Contains("the execution member of the case 'alpha.spec' does not match the catalogue entry")
            && problem.Contains(changedField));
    }

    /// <summary>A golden directory that holds none of the files its catalogue keys name reports each
    /// missing file, whatever the golden kind of the directory is.</summary>
    [Theory]
    [InlineData("DumpedListing", "alpha.txt", "beta.txt")]
    [InlineData("DumpedCfgBlocks", "alpha.json", "beta.json")]
    [InlineData("DumpedCfgPartitions", "alpha.json", "beta.json")]
    [InlineData("GeneratedCode", "alpha.cs.txt", "beta.cs.txt")]
    public void AMissingGoldenFileIsReported(string directoryName, string presentFileName, string missingFileName) {
        // Arrange
        string[] expected = [presentFileName, missingFileName];
        string[] actual = [presentFileName];

        // Act
        GoldenFileSetDiff diff = GoldenCoverageGate.CompareFileSet(expected, actual);

        // Assert
        diff.Missing.Should().Equal(missingFileName);
        diff.Orphan.Should().BeEmpty($"{directoryName} holds every file it was given");
        diff.IsEmpty.Should().BeFalse();
    }

    /// <summary>A golden directory that holds a file no catalogue key names reports it as an orphan,
    /// whatever the golden kind of the directory is.</summary>
    [Theory]
    [InlineData("DumpedListing", "alpha.txt", "orphan.txt")]
    [InlineData("DumpedCfgBlocks", "alpha.json", "orphan.json")]
    [InlineData("DumpedCfgPartitions", "alpha.json", "orphan.json")]
    [InlineData("GeneratedCode", "alpha.cs.txt", "orphan.cs.txt")]
    public void AnOrphanGoldenFileIsReported(string directoryName, string presentFileName, string orphanFileName) {
        // Arrange
        string[] expected = [presentFileName];
        string[] actual = [presentFileName, orphanFileName];

        // Act
        GoldenFileSetDiff diff = GoldenCoverageGate.CompareFileSet(expected, actual);

        // Assert
        diff.Orphan.Should().Equal(orphanFileName);
        diff.Missing.Should().BeEmpty($"{directoryName} holds every expected file");
        diff.IsEmpty.Should().BeFalse();
    }

    /// <summary>A scenario registered under the name of a child program another scenario loads, without
    /// declaring that resource itself, is reported.</summary>
    [Fact]
    public void AResourceProgramNamedByAScenarioThatDoesNotDeclareItIsReported() {
        // Arrange: a parent scenario that loads a child program, and a scenario registered under the
        // child's name that does not declare the resource it is named after
        AsmScenario parent = Scenario("exec_parent", ["DosExecTests/child.com"]);
        AsmScenario child = Scenario("child", []);
        IReadOnlyList<AsmScenarioInventoryEntry> catalogue =
            [AsmScenarioInventory.EntryOf(parent), AsmScenarioInventory.EntryOf(child)];

        // Act
        IReadOnlyList<string> problems = GoldenCoverageGate.ValidateResourceDependencies(catalogue);

        // Assert
        problems.Should().ContainSingle()
            .Which.Should().Contain("'DosExecTests/child.com'")
            .And.Contain("loaded by the scenario 'exec_parent'")
            .And.Contain("named by the scenario 'child'");
    }

    /// <summary>A scenario registered under the name of a child program another scenario loads, declaring
    /// that very resource itself, is still reported: declaring the dependency does not turn a child
    /// program into a standalone scenario, because the scenario does not run that program itself.</summary>
    [Fact]
    public void ASelfDeclaredChildResourceRegisteredStandaloneIsReported() {
        // Arrange: a parent scenario that loads a child program, and a scenario registered under the
        // child's name that declares the same resource as one of its own dependencies while running a
        // different program
        AsmScenario parent = Scenario("exec_parent", ["DosExecTests/child.com"]);
        AsmScenario child = Scenario("child", ["DosExecTests/child.com"]);
        IReadOnlyList<AsmScenarioInventoryEntry> catalogue =
            [AsmScenarioInventory.EntryOf(parent), AsmScenarioInventory.EntryOf(child)];

        // Act
        IReadOnlyList<string> problems = GoldenCoverageGate.ValidateResourceDependencies(catalogue);

        // Assert
        problems.Should().ContainSingle()
            .Which.Should().Contain("'DosExecTests/child.com'")
            .And.Contain("loaded by the scenario 'exec_parent'")
            .And.Contain("named by the scenario 'child'")
            .And.Contain("registers it standalone");
    }

    /// <summary>An inventory entry that reuses the execution key of a real catalogue entry but changes the
    /// named metadata field, so a case execution can disagree with its catalogue entry.</summary>
    /// <param name="real">Catalogue entry whose execution key the impostor reuses.</param>
    /// <param name="changedField">Field label to change, any other field keeps the real value.</param>
    /// <returns>The impostor entry.</returns>
    private static AsmScenarioInventoryEntry ExecutionWithChangedMetadata(
        AsmScenarioInventoryEntry real, string changedField) {
        string identity = changedField == "identity" ? "beta" : real.Identity;
        string variantName = changedField == "variant" ? "changed" : real.VariantName;
        string machineGoldenKey = changedField == "machine-golden key" ? "changed" : real.MachineGoldenKey;
        string generatedSourceKey =
            changedField == "generated-source key" ? "alpha.changed" : real.GeneratedSourceKey;
        return new AsmScenarioInventoryEntry(real.Scenario, identity, variantName, real.ExecutionKey,
            machineGoldenKey, generatedSourceKey, real.PrimaryProgramName);
    }

    /// <summary>An inventory entry whose keys the test states directly, so a broken key space can be
    /// built without touching the catalogue or any committed golden. Its primary program is the entry's
    /// own identity; the registration gate does not use it.</summary>
    /// <param name="identity">Scenario identity of the entry.</param>
    /// <param name="variantName">Variant of the entry, empty for the default variant.</param>
    /// <param name="generatedSourceKey">Generated-source key of the entry.</param>
    /// <returns>The entry.</returns>
    private static AsmScenarioInventoryEntry Entry(string identity, string variantName, string generatedSourceKey) {
        string machineGoldenKey = identity + (variantName.Length == 0 ? "" : "." + variantName);
        return new AsmScenarioInventoryEntry(
            AsmScenario.FromFixture(identity, AsmFixtureCatalog.JmpMov),
            identity,
            variantName,
            AsmScenarioInventory.ExecutionKey(generatedSourceKey, variantName),
            machineGoldenKey,
            generatedSourceKey,
            identity);
    }

    /// <summary>A case whose generated-source key no catalogue scenario produces.</summary>
    /// <returns>The case.</returns>
    private static GeneratedScenarioCase UnknownCase() {
        AsmScenarioInventoryEntry entry = Entry("gamma", "", "gamma.spec");
        return new GeneratedScenarioCase("gamma.spec", entry, [entry]);
    }

    /// <summary>A scenario that runs the jmpmov fixture under its own identity, so the inventory derives
    /// the keys of that identity. The program the scenario runs stays the jmpmov program, so a declared
    /// resource is never the scenario's own primary program.</summary>
    /// <param name="identity">Stable identity of the scenario.</param>
    /// <param name="resourceDependencies">Resource files the scenario needs, relative to the resources root.</param>
    /// <returns>The scenario.</returns>
    private static AsmScenario Scenario(string identity, IReadOnlyList<string> resourceDependencies) {
        return new AsmScenario(identity, _ => {
            AsmFixture fixture = AsmFixtureCatalog.JmpMov();
            AsmRunSettings settings = fixture.Settings with { ScenarioName = identity };
            return new AsmFixture(settings, fixture.ExpectedMemory, fixture.AssertResult);
        }, resourceDependencies);
    }
}
