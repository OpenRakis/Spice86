namespace Spice86.Tests.AsmFixtures;

/// <summary>One fixture scenario: the settings to run it with and the oracle to check after the run.</summary>
internal sealed class AsmFixture {
    /// <summary>Result assertion that checks nothing, for fixtures whose oracle is the expected memory only.</summary>
    public static readonly Action<AsmFixtureResult> NoResultAssertion = _ => { };

    /// <summary>Creates a fixture. A fixture with neither expected memory nor a result assertion cannot be built.</summary>
    /// <param name="settings">Settings to run the fixture with.</param>
    /// <param name="expectedMemory">Expected RAM from linear address 0. May be empty.</param>
    /// <param name="assertResult">Extra program-result checks. May be <see cref="NoResultAssertion"/>.</param>
    /// <exception cref="ArgumentException">There is neither expected memory nor a result assertion.</exception>
    public AsmFixture(AsmRunSettings settings, byte[] expectedMemory, Action<AsmFixtureResult> assertResult) {
        if (expectedMemory.Length == 0 && ReferenceEquals(assertResult, NoResultAssertion)) {
            throw new ArgumentException($"Fixture {settings.BinName} has no behavioral oracle.");
        }
        Settings = settings;
        ExpectedMemory = expectedMemory;
        AssertResult = assertResult;
    }

    /// <summary>Settings to run the fixture with.</summary>
    public AsmRunSettings Settings { get; }

    /// <summary>Expected RAM from linear address 0. May be empty.</summary>
    public byte[] ExpectedMemory { get; }

    /// <summary>Extra program-result checks. May be <see cref="NoResultAssertion"/>.</summary>
    public Action<AsmFixtureResult> AssertResult { get; }

    /// <summary>
    /// Name part that tells this fixture's <c>MachineTest</c> goldens apart from the goldens of another fixture
    /// of the same bin. Empty for the default fixture of a bin.
    /// </summary>
    public string MachineGoldenVariant { get; init; } = "";
}
