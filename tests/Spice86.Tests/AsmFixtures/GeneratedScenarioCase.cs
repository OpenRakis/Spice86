namespace Spice86.Tests.AsmFixtures;

/// <summary>
/// One generated-source golden together with every catalogue scenario and variant that shares it: the
/// discovery member whose run is recorded for generation, and the members run with the compiled
/// override. A group holding several variants shares one generated program, so the member with the
/// empty variant discovers it; a single-member group uses its scenario for both roles.
/// </summary>
internal sealed class GeneratedScenarioCase {
    /// <summary>Creates a case.</summary>
    /// <param name="generatedSourceKey">Generated-source key the case produces and compares.</param>
    /// <param name="discovery">Member whose run is recorded for code generation.</param>
    /// <param name="executions">Members executed with the compiled override, in order.</param>
    public GeneratedScenarioCase(string generatedSourceKey, AsmScenarioInventoryEntry discovery,
        IReadOnlyList<AsmScenarioInventoryEntry> executions) {
        GeneratedSourceKey = generatedSourceKey;
        Discovery = discovery;
        Executions = executions;
        ExecutionKeys = [.. executions.Select(execution => execution.ExecutionKey)];
    }

    /// <summary>Generated-source key the case produces and compares.</summary>
    public string GeneratedSourceKey { get; }

    /// <summary>Member whose run is recorded for code generation.</summary>
    public AsmScenarioInventoryEntry Discovery { get; }

    /// <summary>Members executed with the compiled override, in order.</summary>
    public IReadOnlyList<AsmScenarioInventoryEntry> Executions { get; }

    /// <summary>Execution keys of every member of the case.</summary>
    public IReadOnlyList<string> ExecutionKeys { get; }

    /// <summary>Scenario whose run is recorded for code generation.</summary>
    public AsmScenario DiscoveryScenario => Discovery.Scenario;

    /// <summary>Scenarios executed with the compiled override, in order.</summary>
    public IReadOnlyList<AsmScenario> ExecutionScenarios =>
        [.. Executions.Select(execution => execution.Scenario)];
}
