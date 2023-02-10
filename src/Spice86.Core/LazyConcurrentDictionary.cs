using System.Collections.Concurrent;

namespace Spice86.Core;

public class LazyConcurrentDictionary<TKey, TValue> where TKey : notnull {
    private readonly ConcurrentDictionary<TKey, Lazy<TValue>> _concurrentDictionary;

    public LazyConcurrentDictionary() {
        _concurrentDictionary = new ConcurrentDictionary<TKey, Lazy<TValue>>();
    }

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory) {
        Lazy<TValue> lazyResult = _concurrentDictionary.GetOrAdd(key,
            k => new Lazy<TValue>(() => valueFactory(k), LazyThreadSafetyMode.ExecutionAndPublication));

        return lazyResult.Value;
    }
}