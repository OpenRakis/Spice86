namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using System.Text;

/// <summary>
/// Provides access to DOS environment variables as a dictionary.
/// </summary>
public sealed class EnvironmentVariables : IDictionary<string, string> {
    private readonly Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase);

    internal EnvironmentVariables() {
    }

    /// <summary>
    /// Generates a null-separated block of environment strings.
    /// </summary>
    /// <returns>Null-separated block of environment strings.</returns>
    public byte[] EnvironmentBlock => Encoding.ASCII.GetBytes(EnvironmentString);

    /// <summary>
    /// Generates a null-separated block of environment strings.
    /// </summary>
    /// <returns>Null-separated block of environment strings.</returns>
    public string EnvironmentString {
        get {
            StringBuilder sb = new();
            foreach (KeyValuePair<string, string> pair in variables) {
                sb.Append(pair.Key);
                sb.Append('=');
                sb.Append(pair.Value);
                sb.Append('\0');
            }
            sb.Append('\0');

            return sb.ToString();
        }
    }

    /// <inheritdoc />
    public void Add(string key, string value) => variables.Add(key, value);

    /// <inheritdoc />
    public bool ContainsKey(string key) => variables.ContainsKey(key);

    /// <inheritdoc />
    public ICollection<string> Keys => variables.Keys;

    /// <inheritdoc />
    public bool Remove(string key) => variables.Remove(key);

    /// <inheritdoc />
    public bool TryGetValue(string key, out string value) {
        bool returnValue = variables.TryGetValue(key, out string? innerValue);
        if (innerValue != null) {
            value = innerValue;
        } else {
            value = "";
        }
        return returnValue;
    }

    /// <inheritdoc />
    public ICollection<string> Values => variables.Values;

    /// <inheritdoc />
    public string this[string key] {
        get => variables[key];
        set => variables[key] = value;
    }

    void ICollection<KeyValuePair<string, string>>.Add(KeyValuePair<string, string> item) => ((ICollection<KeyValuePair<string, string>>)variables).Add(item);

    /// <inheritdoc />
    public void Clear() => variables.Clear();
    bool ICollection<KeyValuePair<string, string>>.Contains(KeyValuePair<string, string> item) => ((ICollection<KeyValuePair<string, string>>)variables).Contains(item);
    void ICollection<KeyValuePair<string, string>>.CopyTo(KeyValuePair<string, string>[] array, int arrayIndex) => ((ICollection<KeyValuePair<string, string>>)variables).CopyTo(array, arrayIndex);

    /// <inheritdoc />
    public int Count => variables.Count;
    bool ICollection<KeyValuePair<string, string>>.IsReadOnly => false;
    bool ICollection<KeyValuePair<string, string>>.Remove(KeyValuePair<string, string> item) => ((ICollection<KeyValuePair<string, string>>)variables).Remove(item);

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => variables.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}