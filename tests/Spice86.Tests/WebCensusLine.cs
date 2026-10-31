namespace Spice86.Tests;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ssa;

using System.Linq;

/// <summary>
/// Formats one fixture's <see cref="WebCensus"/> as a line of <c>name=value</c> fields for
/// <c>generated-code/web-census.txt</c>, padded like <see cref="GotoCensusLine"/>.
/// </summary>
internal static class WebCensusLine {
    private const int FixtureFieldWidth = 56;
    private const int FieldWidth = 20;

    /// <summary>The census as one line: <c>fixture</c>, then every field in declaration order.</summary>
    public static string Format(string fixture, WebCensus census) {
        List<(string Name, string Value)> fields = [
            ("fixture", fixture),
            ("webs", census.Webs.ToString()),
            ("pureWebs", census.PureWebs.ToString()),
            ("loadOnlyWebs", census.LoadOnlyWebs.ToString()),
            ("storeOnlyWebs", census.StoreOnlyWebs.ToString()),
            ("throughWebs", census.ThroughWebs.ToString()),
            ("pureUses", census.PureUses.ToString()),
            ("loadOnlyUses", census.LoadOnlyUses.ToString()),
            ("storeOnlyUses", census.StoreOnlyUses.ToString()),
            ("throughUses", census.ThroughUses.ToString()),
            ("pureMultiBlockWebs", census.PureMultiBlockWebs.ToString())
        ];
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
