namespace Spice86.Tests.AsmFixtures;

using Spice86.Core.Emulator.VM;

/// <summary>
/// Disposable preparation scope of one scenario run. It owns everything the run needs that must not be
/// shared with any other run: an independent working directory, pre-run observations such as test-port
/// handlers, post-run observation providers such as a DOS allocation snapshot, and the fixture built for
/// this preparation. A scope is prepared fresh for the discovery run and for every generated execution,
/// and is disposed whether the run succeeds or fails.
/// </summary>
internal sealed class AsmRunScope : IDisposable {
    /// <summary>Name of the pre-run observation holding the CS:IP the machine starts the program at.</summary>
    public const string EntryPointObservation = "entryPoint";

    /// <summary>Name of the observation holding the observer that records interpreter-executed
    /// instructions while an override run executes.</summary>
    public const string InterpreterExecutionObservation = "interpreterExecution";

    /// <summary>Name of the pre-run observation holding the emulator-provided code of the run, captured
    /// on the fresh machine before the program starts.</summary>
    public const string EmulatorCodeInventoryObservation = "emulatorCodeInventory";

    private readonly List<string> _ownedDirectories = new();
    private readonly List<ObservationProvider> _observationProviders = new();
    private AsmFixture? _fixture;

    /// <summary>Creates an empty scope. The scenario factory attaches the fixture to it.</summary>
    public AsmRunScope() {
    }

    /// <summary>Creates a scope around an existing fixture, with no preparation of its own.</summary>
    /// <param name="fixture">Fixture to run in this scope.</param>
    public AsmRunScope(AsmFixture fixture) {
        _fixture = fixture;
    }

    /// <summary>The fixture prepared for this run.</summary>
    /// <exception cref="InvalidOperationException">The scope has no fixture yet.</exception>
    public AsmFixture Fixture {
        get {
            return _fixture ?? throw new InvalidOperationException("This run scope has no fixture attached.");
        }
    }

    /// <summary>Observations of this run that the oracle may read.</summary>
    public AsmRunObservations Observations { get; } = new();

    /// <summary>Attaches the fixture of this preparation to the scope.</summary>
    /// <param name="fixture">Fixture prepared for this run.</param>
    /// <exception cref="InvalidOperationException">The scope already has a fixture.</exception>
    public void AttachFixture(AsmFixture fixture) {
        if (_fixture != null) {
            throw new InvalidOperationException("This run scope already has a fixture attached.");
        }
        _fixture = fixture;
    }

    /// <summary>Creates a fresh working directory for this run and owns its deletion.</summary>
    /// <param name="prefix">Directory name prefix, usually the scenario identity.</param>
    /// <returns>Path of the created directory.</returns>
    public string CreateWorkingDirectory(string prefix) {
        string directory = Path.Join(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        _ownedDirectories.Add(directory);
        return directory;
    }

    /// <summary>Stores a pre-run observation, typically a handler created on the fresh machine.</summary>
    /// <param name="name">Name of the observation.</param>
    /// <param name="observation">Observation to store.</param>
    public void Register(string name, object observation) {
        Observations.Set(name, observation);
    }

    /// <summary>Registers a provider invoked once the program has ended, to snapshot state that only
    /// exists on the finished run such as the DOS allocation map.</summary>
    /// <param name="name">Name of the observation.</param>
    /// <param name="provider">Snapshot function run with the finished machine.</param>
    public void AddObservationProvider(string name, Func<Machine, object> provider) {
        _observationProviders.Add(new ObservationProvider(name, provider));
    }

    /// <summary>Invokes every registered provider with the finished machine and stores its result.</summary>
    /// <param name="machine">Machine of the finished run.</param>
    public void CollectObservations(Machine machine) {
        foreach (ObservationProvider provider in _observationProviders) {
            Observations.Set(provider.Name, provider.Create(machine));
        }
    }

    /// <summary>Deletes the working directories this scope created.</summary>
    public void Dispose() {
        foreach (string directory in _ownedDirectories) {
            DeleteDirectory(directory);
        }
        _ownedDirectories.Clear();
    }

    private static void DeleteDirectory(string directory) {
        if (!Directory.Exists(directory)) {
            return;
        }
        foreach (string filePath in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)) {
            File.SetAttributes(filePath, FileAttributes.Normal);
        }
        Directory.Delete(directory, recursive: true);
    }

    private sealed record ObservationProvider(string Name, Func<Machine, object> Create);
}
