namespace Spice86.Shared.Utils;

using System.Collections.Generic;

/// <summary>
/// A collection of utility methods for working with dictionaries.
/// </summary>
public static class DictionaryUtils {
    /// <summary>
    /// Adds all key-value pairs from dictionary2 to dictionary1, overwriting any existing keys.
    /// </summary>
    /// <typeparam name="K">The type of keys in the dictionaries.</typeparam>
    /// <typeparam name="V">The type of values in the dictionaries.</typeparam>
    /// <param name="dictionary1">The destination dictionary to add the key-value pairs to.</param>
    /// <param name="dictionary2">The source dictionary to copy the key-value pairs from.</param>
    public static void AddAll<K, V>(IDictionary<K, V> dictionary1, IDictionary<K, V> dictionary2) where K : notnull {
        foreach (KeyValuePair<K, V> entry in dictionary2) {
            dictionary1[entry.Key] = entry.Value;
        }
    }
}