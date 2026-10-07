namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Naming;

using System.Linq;

/// <summary>
/// Assigns each item its base name when unique among <paramref name="items"/>, and base name plus
/// <c>_</c> plus <paramref name="suffix"/> for every member of a group that shares a base name.
/// </summary>
internal static class DisambiguatedNames {
    /// <summary>
    /// Builds a dictionary mapping each item to its disambiguated name.
    /// </summary>
    /// <typeparam name="T">The type of items.</typeparam>
    /// <param name="items">The items to name.</param>
    /// <param name="baseName">Function that returns the base name for an item.</param>
    /// <param name="suffix">Function that returns the suffix for an item.</param>
    /// <returns>A dictionary mapping each item to its disambiguated name.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the resulting names still contain a duplicate.</exception>
    public static Dictionary<T, string> Build<T>(
        IEnumerable<T> items,
        Func<T, string> baseName,
        Func<T, string> suffix) where T : notnull {
        Dictionary<string, List<T>> groups = items
            .GroupBy(baseName)
            .ToDictionary(group => group.Key, group => group.ToList());

        Dictionary<T, string> result = [];
        foreach (KeyValuePair<string, List<T>> group in groups) {
            if (group.Value.Count == 1) {
                result[group.Value[0]] = group.Key;
            } else {
                foreach (T item in group.Value) {
                    result[item] = $"{group.Key}_{suffix(item)}";
                }
            }
        }

        string? duplicate = result.Values.GroupBy(name => name).FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicate != null) {
            throw new InvalidOperationException($"Disambiguation produced duplicate name: {duplicate}");
        }

        return result;
    }
}
