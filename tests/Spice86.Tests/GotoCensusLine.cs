namespace Spice86.Tests;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;

using System.Linq;

/// <summary>
/// Formats one fixture's <see cref="GotoCensus"/> as a line of <c>name=value</c> fields for
/// <c>generated-code/goto-census.txt</c>, in the <see cref="GotoKeepReason"/> declaration order, padded like
/// <see cref="GeneratedCodeMetrics.ToLine"/>.
/// </summary>
internal static class GotoCensusLine {
    private const int FixtureFieldWidth = 56;
    private const int FieldWidth = 15;

    /// <summary>The census as one line: <c>fixture</c>, <c>total</c>, then one field per reason, table order.</summary>
    public static string Format(string fixture, GotoCensus census) {
        List<(string Name, string Value)> fields = [("fixture", fixture), ("total", census.Total.ToString())];
        foreach (GotoKeepReason reason in Enum.GetValues<GotoKeepReason>()) {
            fields.Add((FieldName(reason), census.Count(reason).ToString()));
        }
        return string.Concat(fields.Select((field, index) => Pad($"{field.Name}={field.Value}", WidthOf(index, fields.Count))));
    }

    private static string FieldName(GotoKeepReason reason) {
        string name = reason.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static int WidthOf(int index, int count) {
        if (index == 0) {
            return FixtureFieldWidth;
        }
        if (index == count - 1) {
            return 0;
        }
        return FieldWidth;
    }

    private static string Pad(string value, int width) {
        if (width == 0) {
            return value;
        }
        return value.PadRight(width - 1) + " ";
    }
}
