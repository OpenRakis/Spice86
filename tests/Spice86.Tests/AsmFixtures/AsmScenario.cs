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

    /// <summary>Why this scenario's observable state depends on timing, or <see langword="null"/> when
    /// it does not. The generated run still executes and checks the fixture oracle; only the
    /// interpreted-vs-generated final-state differential is relaxed, with this reason.</summary>
    public string? TimingDependentReason { get; private set; }

    /// <summary>Returns the same scenario marked as timing-dependent.</summary>
    /// <param name="reason">Why the scenario's observable state depends on timing.</param>
    /// <returns>The scenario, with the timing-dependent reason recorded.</returns>
    public AsmScenario WithTimingDependentState(string reason) {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        TimingDependentReason = reason;
        return this;
    }

    /// <summary>Final-state parts the interpreted-vs-generated comparison leaves out for this
    /// scenario, or <see cref="FinalStateParts.None"/> when every part is compared.</summary>
    public FinalStateParts UncomparedParts { get; private set; } = FinalStateParts.None;

    /// <summary>Why <see cref="UncomparedParts"/> are not compared, or empty when none are
    /// left out.</summary>
    public string UncomparedPartsReason { get; private set; } = "";

    /// <summary>RAM ranges the interpreted-vs-generated comparison leaves out for this scenario,
    /// each with the reason it cannot be compared.</summary>
    public IReadOnlyList<(uint Address, int Length, string Reason)> UncomparedRamRanges { get; private set; } = [];

    /// <summary>Returns the same scenario with the given RAM range left out of the
    /// interpreted-vs-generated comparison.</summary>
    /// <param name="linearAddress">Start of the range, as a linear address.</param>
    /// <param name="length">Length of the range in bytes.</param>
    /// <param name="reason">Why the range cannot be compared between the two runs.</param>
    /// <returns>The scenario, with the uncompared range appended.</returns>
    public AsmScenario WithUncomparedRam(uint linearAddress, int length, string reason) {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (length <= 0) {
            throw new ArgumentException("An uncompared RAM range needs a positive length.", nameof(length));
        }
        UncomparedRamRanges = [.. UncomparedRamRanges, (linearAddress, length, reason)];
        return this;
    }

    /// <summary>Returns the same scenario with the given final-state parts left out of the
    /// interpreted-vs-generated comparison.</summary>
    /// <param name="parts">The parts not to compare.</param>
    /// <param name="reason">Why those parts cannot be compared between the two runs.</param>
    /// <returns>The scenario, with the uncompared parts recorded.</returns>
    public AsmScenario WithUncomparedParts(FinalStateParts parts, string reason) {
        if (parts == FinalStateParts.None) {
            throw new ArgumentException("Uncompared parts cannot be None.", nameof(parts));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        UncomparedParts = parts;
        UncomparedPartsReason = reason;
        return this;
    }

    /// <summary>Creates a scenario with the same identity, resources and preparation, but another oracle.
    /// Used by discrimination tests that prove a wrong expectation fails in both execution modes.</summary>
    /// <param name="rewriteOracle">Rewrites the fixture the preparation produced.</param>
    /// <returns>The scenario with the rewritten oracle.</returns>
    public AsmScenario WithOracle(Func<AsmFixture, AsmFixture> rewriteOracle) {
        return new AsmScenario(Identity, scope => rewriteOracle(_prepareFixture(scope)), ResourceDependencies) {
            PrimaryResourceFileName = PrimaryResourceFileName,
            TimingDependentReason = TimingDependentReason,
            UncomparedParts = UncomparedParts,
            UncomparedPartsReason = UncomparedPartsReason,
            UncomparedRamRanges = UncomparedRamRanges
        };
    }

    /// <summary>Prepares a fresh run of this scenario: a new scope, a new working directory when the scenario
    /// needs one, new observation handlers and a new fixture holding the one oracle.</summary>
    /// <returns>The prepared run, owned by the caller, which disposes it on success or failure.</returns>
    public AsmRunScope Prepare() => PrepareInto(new AsmRunScope());

    /// <summary>Prepares a run of this scenario under an earlier run's key, so its host paths match
    /// that run's. Used for the generated execution of a variant, which must reproduce the host
    /// environment its interpreted baseline ran in.</summary>
    /// <param name="runKey">Run key of the interpreted baseline run.</param>
    /// <returns>The prepared run, owned by the caller, which disposes it on success or failure.</returns>
    public AsmRunScope Prepare(string runKey) => PrepareInto(new AsmRunScope(runKey));

    private AsmRunScope PrepareInto(AsmRunScope scope) {
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
