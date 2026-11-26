namespace Spice86.Tests;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using System.Linq;

/// <summary>
/// Formats one fixture's untyped-method census as a line of <c>name=value</c> fields for
/// <c>generated-code/untyped-census.txt</c>, padded like <see cref="GotoCensusLine"/>.
/// </summary>
internal static class UntypedCensusLine {
    private const int FixtureFieldWidth = 56;
    private const int FieldWidth = 24;

    /// <summary>The census as one line: <c>fixture</c>, <c>untypedMethods</c>, then one field per
    /// <see cref="UntypedReason"/> holding how many methods that reason refused.</summary>
    public static string Format(string fixture, IReadOnlyList<UntypedMethod> untyped) {
        List<(string Name, string Value)> fields = [
            ("fixture", fixture),
            ("untypedMethods", untyped.Count.ToString())
        ];
        fields.AddRange(Enum.GetValues<UntypedReason>().Select(reason =>
            ($"{char.ToLowerInvariant(reason.ToString()[0])}{reason.ToString()[1..]}",
                untyped.Count(method => method.Reason == reason).ToString())));
        return string.Concat(fields.Select((field, index) => Pad($"{field.Name}={field.Value}", WidthOf(index, fields.Count))));
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
