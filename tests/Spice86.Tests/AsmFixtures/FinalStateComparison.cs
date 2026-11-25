namespace Spice86.Tests.AsmFixtures;

/// <summary>
/// How the interpreted-vs-generated final-state comparison applies to one execution: a timing
/// reason that leaves RAM and the extended regions out, and final-state parts left out of the
/// comparison with their reason.
/// </summary>
internal sealed record FinalStateComparison(string? TimingDependentReason,
    FinalStateParts UncomparedParts, string UncomparedPartsReason) {
    /// <summary>Every part compared, nothing timing-dependent.</summary>
    public static FinalStateComparison Strict { get; } = new(null, FinalStateParts.None, "");

    /// <summary>The comparison a scenario's marks describe.</summary>
    public static FinalStateComparison Of(AsmScenario scenario) =>
        new(scenario.TimingDependentReason, scenario.UncomparedParts, scenario.UncomparedPartsReason);
}
