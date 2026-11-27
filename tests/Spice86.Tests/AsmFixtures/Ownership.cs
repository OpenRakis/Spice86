namespace Spice86.Tests.AsmFixtures;

internal static class Ownership {
    /// <summary>Runs <paramref name="action"/> and returns <paramref name="owned"/>, which then
    /// belongs to the caller; disposes it when <paramref name="action"/> throws.</summary>
    internal static TOwned DisposeOnFailure<TOwned>(TOwned owned, Action action)
            where TOwned : IDisposable {
        try {
            action();
            return owned;
        } catch {
            owned.Dispose();
            throw;
        }
    }
}
