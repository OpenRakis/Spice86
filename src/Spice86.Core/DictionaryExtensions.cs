namespace Spice86.Core;

using System;
using System.Collections.Generic;

internal static class DictionaryExtensions {
    /// <summary>
    /// Like "GetValueOrDefault" but adds the value to the dict if not found.<br/>
    /// </summary>
    /// <typeparam name="TKey">The generic key type</typeparam>
    /// <typeparam name="TValue">The generic return type</typeparam>
    /// <param name="dict">The input <see cref="IDictionary{TKey, TValue}"/></param>
    /// <param name="key">The input key to use</param>
    /// <param name="backupValue">The value to insert and return if the value is not found.</param>
    /// <returns>The found or computed value</returns>
    public static TValue ComputeIfAbsent<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue backupValue) {
        if (dict.TryGetValue(key, out TValue? value)) {
            if (value is not null) {
                return value;
            }
        }
        value = backupValue;
        _ = dict.TryAdd(key, value);
        return value;
    }
}