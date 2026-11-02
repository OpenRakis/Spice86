namespace Spice86.Tests.AsmFixtures;

/// <summary>
/// Per-run behavioral observations an oracle may read in addition to memory and CPU state: test-port
/// handlers, DOS allocation snapshots and any other typed result captured during or after one run.
/// Every run gets its own bag, so an oracle never reads the observations of another run.
/// </summary>
internal sealed class AsmRunObservations {
    private readonly Dictionary<string, object> _observations = new(StringComparer.Ordinal);

    /// <summary>Stores one observation under the given name, replacing any previous value of that name.</summary>
    /// <param name="name">Name of the observation.</param>
    /// <param name="observation">Observation to store.</param>
    public void Set<T>(string name, T observation) {
        _observations[name] = observation ?? throw new ArgumentNullException(nameof(observation));
    }

    /// <summary>Reads the observation stored under the given name.</summary>
    /// <typeparam name="T">Expected type of the observation.</typeparam>
    /// <param name="name">Name of the observation.</param>
    /// <returns>The stored observation.</returns>
    /// <exception cref="InvalidOperationException">No observation of that name, or it is not of the expected type.</exception>
    public T Get<T>(string name) {
        if (!_observations.TryGetValue(name, out object? observation)) {
            throw new InvalidOperationException(
                $"No observation named '{name}' was captured. Observations present: [{string.Join(", ", _observations.Keys)}].");
        }
        if (observation is not T typed) {
            throw new InvalidOperationException(
                $"Observation '{name}' is a {observation.GetType().Name}, not a {typeof(T).Name}.");
        }
        return typed;
    }
}
