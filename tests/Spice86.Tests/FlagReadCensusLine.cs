namespace Spice86.Tests;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ssa;

using System.Linq;

/// <summary>
/// Formats one fixture's <see cref="FlagReadCensus"/> as a line of <c>name=value</c> fields for
/// <c>generated-code/flag-read-census.txt</c>: the three groups in their enum declaration orders,
/// padded like <see cref="GotoCensusLine"/>.
/// </summary>
internal static class FlagReadCensusLine {
    private const int FixtureFieldWidth = 56;
    private const int FieldWidth = 20;

    /// <summary>The census as one line: <c>fixture</c>, <c>total</c>, then every field of the three groups.</summary>
    public static string Format(string fixture, FlagReadCensus census) {
        List<(string Name, string Value)> fields = [("fixture", fixture), ("total", census.Total.ToString())];
        foreach (FlagSetterKind kind in Enum.GetValues<FlagSetterKind>()) {
            fields.Add((FieldName(kind.ToString()), census.Count(kind).ToString()));
        }
        foreach (FlagSetterDistance distance in Enum.GetValues<FlagSetterDistance>()) {
            fields.Add((FieldName(distance.ToString()), census.Count(distance).ToString()));
        }
        foreach (FlagPropagationBlocker blocker in Enum.GetValues<FlagPropagationBlocker>()) {
            fields.Add((FieldName(blocker.ToString()), census.Count(blocker).ToString()));
        }
        return string.Concat(fields.Select((field, index) => Pad($"{field.Name}={field.Value}", WidthOf(index, fields.Count))));
    }

    private static string FieldName(string name) => char.ToLowerInvariant(name[0]) + name[1..];

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
