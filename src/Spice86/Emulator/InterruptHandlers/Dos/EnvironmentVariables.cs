namespace Spice86.Emulator.InterruptHandlers.Dos;

using System;
using System.Collections.Generic;
using System.Text;


/// <summary>
/// Provides access to DOS environment variables as a dictionary.
/// </summary>
public sealed class EnvironmentVariables : IDictionary<string, string> {
    private readonly Dictionary<string, string> variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    internal EnvironmentVariables() {
    }

    /// <summary>
    /// Generates a null-separated block of environment strings.
    /// </summary>
    /// <returns>Null-separated block of environment strings.</returns>
    internal byte[] GetEnvironmentBlock() {
        var sb = new StringBuilder();
        foreach (KeyValuePair<string, string> pair in variables) {
            sb.Append(pair.Key);
            sb.Append('=');
            sb.Append(pair.Value);
            sb.Append('\0');
        }
        sb.Append('\0');

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    public void Add(string key, string value) => variables.Add(key, value);
    public bool ContainsKey(string key) => variables.ContainsKey(key);
    public ICollection<string> Keys => variables.Keys;
    public bool Remove(string key) => variables.Remove(key);
    public bool TryGetValue(string key, out string value) {
        var returnValue = variables.TryGetValue(key, out var innerValue);
        if(innerValue != null) {
            value = innerValue;
        }
        else {
            value = "";
        }
        return returnValue;
    }

    public ICollection<string> Values => variables.Values;
    public string this[string key] {
        get => variables[key];
        set => variables[key] = value;
    }

    void ICollection<KeyValuePair<string, string>>.Add(KeyValuePair<string, string> item) => ((ICollection<KeyValuePair<string, string>>)variables).Add(item);
    public void Clear() => variables.Clear();
    bool ICollection<KeyValuePair<string, string>>.Contains(KeyValuePair<string, string> item) => ((ICollection<KeyValuePair<string, string>>)variables).Contains(item);
    void ICollection<KeyValuePair<string, string>>.CopyTo(KeyValuePair<string, string>[] array, int arrayIndex) => ((ICollection<KeyValuePair<string, string>>)variables).CopyTo(array, arrayIndex);
    public int Count => variables.Count;
    bool ICollection<KeyValuePair<string, string>>.IsReadOnly => false;
    bool ICollection<KeyValuePair<string, string>>.Remove(KeyValuePair<string, string> item) => ((ICollection<KeyValuePair<string, string>>)variables).Remove(item);

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => variables.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
