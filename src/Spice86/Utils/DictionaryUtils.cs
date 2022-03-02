namespace Spice86.Utils;

using System.Collections.Generic;

public class DictionaryUtils {
    public static void AddAll<K, V>(IDictionary<K, V> dictionary1, IDictionary<K, V> dictionary2) where K : notnull {
        foreach (KeyValuePair<K, V> entry in dictionary2) {
            dictionary1[entry.Key] = entry.Value;
        }
    }
}