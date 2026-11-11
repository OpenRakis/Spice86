namespace Spice86.Tests.AsmFixtures;

using Spice86.Core.Emulator.VM;

/// <summary>One fixture scenario: the settings to run it with and the oracle to check after the run.</summary>
internal sealed class AsmFixture {
    /// <summary>Result assertion that checks nothing, for fixtures whose oracle is the expected memory only.</summary>
    public static readonly Action<AsmFixtureResult> NoResultAssertion = _ => { };

    private static readonly Action<AsmRunScope, Machine> NoEngineAssertions = (_, _) => { };

    private readonly Action<AsmRunScope, Machine> _engineAssertions;

    /// <summary>Creates a fixture. A fixture with neither expected memory nor a result assertion cannot be built.</summary>
    /// <param name="settings">Settings to run the fixture with.</param>
    /// <param name="expectedMemory">Expected RAM from linear address 0. May be empty.</param>
    /// <param name="assertResult">Extra program-result checks. May be <see cref="NoResultAssertion"/>.</param>
    /// <exception cref="ArgumentException">There is neither expected memory nor a result assertion.</exception>
    public AsmFixture(AsmRunSettings settings, byte[] expectedMemory, Action<AsmFixtureResult> assertResult)
        : this(settings, expectedMemory, assertResult, NoEngineAssertions) {
    }

    private AsmFixture(AsmRunSettings settings, byte[] expectedMemory, Action<AsmFixtureResult> assertResult,
        Action<AsmRunScope, Machine> engineAssertions) {
        if (expectedMemory.Length == 0 && ReferenceEquals(assertResult, NoResultAssertion)) {
            throw new ArgumentException($"Fixture {settings.BinName} has no behavioral oracle.");
        }
        Settings = settings;
        ExpectedMemory = expectedMemory;
        AssertResult = assertResult;
        _engineAssertions = engineAssertions;
    }

    /// <summary>Returns a fixture with the same settings, expected memory and oracle, plus engine checks
    /// that every emulated run of it performs after the oracle.</summary>
    /// <param name="engineAssertions">Checks of the finished machine's engine state, run after the oracle.</param>
    /// <returns>The fixture with the engine assertions attached.</returns>
    public AsmFixture WithEngineAssertions(Action<AsmRunScope, Machine> engineAssertions) {
        return new AsmFixture(Settings, ExpectedMemory, AssertResult, engineAssertions);
    }

    /// <summary>Runs the fixture's engine assertions on the finished run.</summary>
    /// <param name="scope">Prepared run, holding the observations of that run.</param>
    /// <param name="machine">Machine of the finished run.</param>
    public void AssertEngine(AsmRunScope scope, Machine machine) {
        _engineAssertions(scope, machine);
    }

    /// <summary>Settings to run the fixture with.</summary>
    public AsmRunSettings Settings { get; }

    /// <summary>Expected RAM from linear address 0. May be empty.</summary>
    public byte[] ExpectedMemory { get; }

    /// <summary>Extra program-result checks. May be <see cref="NoResultAssertion"/>.</summary>
    public Action<AsmFixtureResult> AssertResult { get; }
}
