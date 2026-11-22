namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Shared.Emulator.Memory;

using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// Numbers the branch probes of one generated program and remembers what each one wraps. The printers ask it
/// for the text of a probe while the body of a method is printed. Site numbers are given in print order, so
/// the same program generated twice gets the same numbers.
/// </summary>
internal sealed partial class BranchProbeSites {
    private readonly List<BranchProbeSite> _sites = [];

    /// <summary>Name of the method whose body is being printed.</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>Address of the last instruction whose statements started printing, or <c>null</c> before
    /// the first one of the method.</summary>
    public SegmentedAddress? Address { get; set; }

    /// <summary>Every site a printer asked for, in number order. A printer may print an expression and drop
    /// the text, so some of these sites are absent from the source: see <see cref="SitesIn"/>.</summary>
    public IReadOnlyList<BranchProbeSite> Sites => _sites;

    /// <summary>The text of a probe around a condition: <c>Branch(site, condition)</c>.</summary>
    /// <param name="kind">The statement or expression the condition belongs to.</param>
    /// <param name="condition">The printed condition.</param>
    public string Branch(BranchProbeKind kind, string condition) {
        int site = Add(kind, condition);
        return $"{nameof(CSharpOverrideHelper.Branch)}({site}, {condition})";
    }

    /// <summary>The text of a probe around the value of a <c>switch</c>: <c>Selector(site, value)</c>.</summary>
    /// <param name="value">The printed value.</param>
    public string Selector(string value) {
        int site = Add(BranchProbeKind.Switch, value);
        return $"{nameof(CSharpOverrideHelper.Selector)}({site}, {value})";
    }

    /// <summary>The sites whose probe is present in <paramref name="sources"/>, in number order.</summary>
    /// <param name="sources">The printed method texts of the program.</param>
    public IReadOnlyList<BranchProbeSite> SitesIn(IEnumerable<string> sources) {
        HashSet<int> printed = [];
        foreach (string source in sources) {
            foreach (Match match in ProbeCall().Matches(source)) {
                printed.Add(int.Parse(match.Groups[1].Value));
            }
        }
        return _sites.Where(site => printed.Contains(site.Id)).ToList();
    }

    private int Add(BranchProbeKind kind, string text) {
        int site = _sites.Count;
        _sites.Add(new BranchProbeSite(site, kind, Method, Address, text));
        return site;
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9_.])(?:Branch|Selector)\((\d+), ")]
    private static partial Regex ProbeCall();
}
