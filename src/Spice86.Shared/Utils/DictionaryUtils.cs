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

    /// <summary>
    /// Returns the list for <paramref name="key"/>, creating and inserting an empty list if absent.
    /// </summary>
    public static List<TValue> GetOrAddList<TKey, TValue>(Dictionary<TKey, List<TValue>> dictionary, TKey key)
        where TKey : notnull {
        if (!dictionary.TryGetValue(key, out List<TValue>? values)) {
            values = new();
            dictionary[key] = values;
        }
        return values;
    }

    /// <summary>
    /// Removes <paramref name="value"/> from the collection stored at <paramref name="key"/> and drops
    /// the key entirely when that collection becomes empty. No-op when the key is absent or the value
    /// was not present.
    /// </summary>
    /// <returns><c>true</c> when the value was removed, <c>false</c> otherwise.</returns>
    public static bool RemoveFromCollection<TKey, TValue, TCollection>(
        IDictionary<TKey, TCollection> dictionary, TKey key, TValue value)
        where TKey : notnull
        where TCollection : class, ICollection<TValue> {
        if (!dictionary.TryGetValue(key, out TCollection? collection) || !collection.Remove(value)) {
            return false;
        }
        if (collection.Count == 0) {
            dictionary.Remove(key);
        }
        return true;
    }
}