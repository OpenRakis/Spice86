namespace Spice86.Core.Emulator.OperatingSystem.Batch;

using System.Collections.Generic;
using System.Text;

/// <summary>
/// Tokenises MOUNT / IMGMOUNT argument strings respecting double-quoted tokens,
/// matching the behaviour of the DOSBox Staging shell tokeniser.
/// </summary>
internal static class BatchArgumentParser {
    /// <summary>
    /// Splits <paramref name="input"/> on ASCII whitespace, treating text enclosed in
    /// double-quotes as a single token. The surrounding quotes are stripped from the result.
    /// </summary>
    internal static string[] SplitWithQuotes(string input) {
        List<string> parts = new();
        bool inQuotes = false;
        StringBuilder current = new();
        foreach (char c in input) {
            if (c == '"') {
                inQuotes = !inQuotes;
            } else if ((c == ' ' || c == '\t') && !inQuotes) {
                if (current.Length > 0) {
                    parts.Add(current.ToString());
                    current.Clear();
                }
            } else {
                current.Append(c);
            }
        }
        if (current.Length > 0) {
            parts.Add(current.ToString());
        }
        return parts.ToArray();
    }
}
