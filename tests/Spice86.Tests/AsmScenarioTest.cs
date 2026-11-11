namespace Spice86.Tests;

using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Runs every catalogue scenario, one case per generated-source golden: each non-discovery variant
/// first runs interpreted against its oracle, engine assertions and machine goldens, then the
/// discovery run records the program, its C# is generated, compiled with the metrics gate, and every
/// variant executes with the compiled override against its own oracle, ending with the
/// generated-source golden. The cases come from <see cref="GeneratedScenarioCatalog"/> and are split
/// into <see cref="ShardCount"/> shards so the shards run in parallel.
/// </summary>
public static class AsmScenarioTest {
    /// <summary>Number of shards the catalogue cases are split into.</summary>
    public const int ShardCount = 8;

    /// <summary>Generated-source keys of shard <paramref name="shard"/>: every
    /// <see cref="ShardCount"/>-th case of the catalogue, starting at that shard.</summary>
    /// <param name="shard">Zero-based shard index.</param>
    /// <returns>The generated-source keys of the shard, as xUnit theory data.</returns>
    public static IEnumerable<object[]> Keys(int shard) {
        IReadOnlyList<GeneratedScenarioCase> cases = GeneratedScenarioCatalog.All;
        for (int index = shard; index < cases.Count; index += ShardCount) {
            yield return [cases[index].GeneratedSourceKey];
        }
    }

    /// <summary>Runs one generated case: interpreted checks, discovery, compilation, every guarded
    /// execution with its own oracle and the generated-source golden they share.</summary>
    /// <param name="generatedSourceKey">Generated-source key of the case to run.</param>
    public static void Run(string generatedSourceKey) {
        GeneratedScenarioCase scenarioCase = GeneratedScenarioCatalog.Case(generatedSourceKey);
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(
            scenarioCase.DiscoveryScenario, scenarioCase.ExecutionScenarios);
    }

    /// <summary>Shard 0 of the catalogue cases.</summary>
    public sealed class Shard0 {
        /// <summary>Runs one case of shard 0.</summary>
        [Theory]
        [MemberData(nameof(Keys), 0, MemberType = typeof(AsmScenarioTest))]
        public void CatalogueCase(string generatedSourceKey) => Run(generatedSourceKey);
    }

    /// <summary>Shard 1 of the catalogue cases.</summary>
    public sealed class Shard1 {
        /// <summary>Runs one case of shard 1.</summary>
        [Theory]
        [MemberData(nameof(Keys), 1, MemberType = typeof(AsmScenarioTest))]
        public void CatalogueCase(string generatedSourceKey) => Run(generatedSourceKey);
    }

    /// <summary>Shard 2 of the catalogue cases.</summary>
    public sealed class Shard2 {
        /// <summary>Runs one case of shard 2.</summary>
        [Theory]
        [MemberData(nameof(Keys), 2, MemberType = typeof(AsmScenarioTest))]
        public void CatalogueCase(string generatedSourceKey) => Run(generatedSourceKey);
    }

    /// <summary>Shard 3 of the catalogue cases.</summary>
    public sealed class Shard3 {
        /// <summary>Runs one case of shard 3.</summary>
        [Theory]
        [MemberData(nameof(Keys), 3, MemberType = typeof(AsmScenarioTest))]
        public void CatalogueCase(string generatedSourceKey) => Run(generatedSourceKey);
    }

    /// <summary>Shard 4 of the catalogue cases.</summary>
    public sealed class Shard4 {
        /// <summary>Runs one case of shard 4.</summary>
        [Theory]
        [MemberData(nameof(Keys), 4, MemberType = typeof(AsmScenarioTest))]
        public void CatalogueCase(string generatedSourceKey) => Run(generatedSourceKey);
    }

    /// <summary>Shard 5 of the catalogue cases.</summary>
    public sealed class Shard5 {
        /// <summary>Runs one case of shard 5.</summary>
        [Theory]
        [MemberData(nameof(Keys), 5, MemberType = typeof(AsmScenarioTest))]
        public void CatalogueCase(string generatedSourceKey) => Run(generatedSourceKey);
    }

    /// <summary>Shard 6 of the catalogue cases.</summary>
    public sealed class Shard6 {
        /// <summary>Runs one case of shard 6.</summary>
        [Theory]
        [MemberData(nameof(Keys), 6, MemberType = typeof(AsmScenarioTest))]
        public void CatalogueCase(string generatedSourceKey) => Run(generatedSourceKey);
    }

    /// <summary>Shard 7 of the catalogue cases.</summary>
    public sealed class Shard7 {
        /// <summary>Runs one case of shard 7.</summary>
        [Theory]
        [MemberData(nameof(Keys), 7, MemberType = typeof(AsmScenarioTest))]
        public void CatalogueCase(string generatedSourceKey) => Run(generatedSourceKey);
    }
}
