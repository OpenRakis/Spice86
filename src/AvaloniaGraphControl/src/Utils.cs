#if NETSTANDARD2_0

using System.Collections.Generic;

namespace AvaloniaGraphControl
{
  internal static class Utils
  {
    public static TValue GetValueOrDefault<TKey, TValue>(
      this IDictionary<TKey, TValue> dic, TKey key, TValue defaultValue = default)
      => dic.TryGetValue(key, out TValue value)
          ? value
          : defaultValue;
  }
}

#endif