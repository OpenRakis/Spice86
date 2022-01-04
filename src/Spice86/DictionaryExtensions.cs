namespace Spice86;

using System;
using System.Collections.Generic;

internal static class DictionaryExtensions
{
    /// <summary>
    /// Like <see cref="GetValueOrDefault"/> but only evaluates the lambda if needed.
    /// </summary>
    /// <typeparam name="TKey">The generic key type</typeparam>
    /// <typeparam name="TValue">The generic return type</typeparam>
    /// <param name="dict">The input <see cref="IDictionary{TKey, TValue}"/></param>
    /// <param name="key">The input key to use</param>
    /// <param name="lambda">The lambda to call if the value is not found.</param>
    /// <returns>The found or computed value</returns>
    public static TValue ComputeIfAbsent<TKey, TValue>(this IDictionary<TKey, TValue>? dict, TKey key, Func<TValue> lambda)
    {
        if(dict is null)
        {
            return lambda.Invoke();
        }
        if(dict.TryGetValue(key, out var value))
        {
            return value;
        }
        return lambda.Invoke();
    }
}
