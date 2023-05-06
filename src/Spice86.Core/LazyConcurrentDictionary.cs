using System.Collections.Concurrent;

namespace Spice86.Core;

/// <summary>
/// A thread-safe dictionary implementation that uses lazy initialization for values.
/// </summary>
/// <typeparam name="TKey">The type of the dictionary keys.</typeparam>
/// <typeparam name="TValue">The type of the dictionary values.</typeparam>
public class LazyConcurrentDictionary<TKey, TValue> where TKey : notnull {
    private readonly ConcurrentDictionary<TKey, Lazy<TValue>> _concurrentDictionary;

    /// <summary>
    /// Initializes a new instance of the LazyConcurrentDictionary class.
    /// </summary>
    public LazyConcurrentDictionary() {
        _concurrentDictionary = new ConcurrentDictionary<TKey, Lazy<TValue>>();
    }

    /// <summary>
    /// Gets the value associated with the specified key or adds a new value if the key does not exist.
    /// </summary>
    /// <param name="key">The key of the value to get or add.</param>
    /// <param name="valueFactory">A function that creates the value to add if the key does not exist.</param>
    /// <returns>The value associated with the specified key or the new value created by the valueFactory.</returns>
    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory) {
        Lazy<TValue> lazyResult = _concurrentDictionary.GetOrAdd(key,
            k => new Lazy<TValue>(() => valueFactory(k), LazyThreadSafetyMode.ExecutionAndPublication));

        return lazyResult.Value;
    }
}
