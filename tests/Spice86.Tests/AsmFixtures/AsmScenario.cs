namespace Spice86.Tests.AsmFixtures;

/// <summary>
/// One runnable ASM test scenario: a stable identity independent of host paths, the resources it
/// depends on, a fresh-run preparation, and one behavioral oracle. Every run of the scenario
/// (discovery, interpreted validation and generated execution) prepares its own <see cref="AsmRunScope"/>
/// from the same definition, so no state of one run can leak into another.
/// </summary>
internal sealed class AsmScenario {
    private readonly Func<AsmRunScope, AsmFixture> _prepareFixture;

    /// <summary>Creates a scenario.</summary>
    /// <param name="identity">Stable logical name of the scenario, never derived from a temporary path.</param>
    /// <param name="prepareFixture">Builds the fixture of one run from that run's scope.</param>
    /// <param name="resourceDependencies">Resource files the scenario needs, relative to the test resources root.</param>
    public AsmScenario(string identity, Func<AsmRunScope, AsmFixture> prepareFixture,
        IReadOnlyList<string> resourceDependencies) {
        if (string.IsNullOrWhiteSpace(identity)) {
            throw new ArgumentException("A scenario needs a non-empty identity.", nameof(identity));
        }
        Identity = identity;
        _prepareFixture = prepareFixture;
        ResourceDependencies = resourceDependencies;
    }

    /// <summary>Creates a scenario without preparation, around a fixture factory that returns a fresh fixture per call.</summary>
    /// <param name="identity">Stable logical name of the scenario.</param>
    /// <param name="factory">Builds a fresh fixture for each run.</param>
    /// <returns>The scenario, with no resource dependencies.</returns>
    public static AsmScenario FromFixture(string identity, Func<AsmFixture> factory) {
        return new AsmScenario(identity, _ => factory(), []);
    }

    /// <summary>Stable logical name of the scenario, independent of host paths.</summary>
    public string Identity { get; }

    /// <summary>Resource files the scenario needs, relative to the test resources root. Helper programs that
    /// run under a parent scenario are dependencies, not standalone scenarios.</summary>
    public IReadOnlyList<string> ResourceDependencies { get; }

    /// <summary>Resource file, relative to the test resources root, the scenario runs, when its run renames
    /// that program to another file name. Empty when the started program is the resource itself.</summary>
    public string PrimaryResourceFileName { get; init; } = "";

    /// <summary>Creates a scenario with the same identity, resources and preparation, but another oracle.
    /// Used by discrimination tests that prove a wrong expectation fails in both execution modes.</summary>
    /// <param name="rewriteOracle">Rewrites the fixture the preparation produced.</param>
    /// <returns>The scenario with the rewritten oracle.</returns>
    public AsmScenario WithOracle(Func<AsmFixture, AsmFixture> rewriteOracle) {
        return new AsmScenario(Identity, scope => rewriteOracle(_prepareFixture(scope)), ResourceDependencies) {
            PrimaryResourceFileName = PrimaryResourceFileName
        };
    }

    /// <summary>Prepares a fresh run of this scenario: a new scope, a new working directory when the scenario
    /// needs one, new observation handlers and a new fixture holding the one oracle.</summary>
    /// <returns>The prepared run, owned by the caller, which disposes it on success or failure.</returns>
    public AsmRunScope Prepare() {
        AsmRunScope scope = new();
        bool succeeded = false;
        try {
            scope.AttachFixture(_prepareFixture(scope));
            succeeded = true;
            return scope;
        } finally {
            if (!succeeded) {
                scope.Dispose();
            }
        }
    }
}
