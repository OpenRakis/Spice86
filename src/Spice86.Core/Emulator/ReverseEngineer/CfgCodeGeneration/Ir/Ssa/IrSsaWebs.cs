namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

using System.Linq;

/// <summary>
/// The webs of one method's IR SSA: the versions of one machine value that must share one storage
/// place, found with a union-find over the phis and the partial definitions. A phi joins its result
/// with each operand; a partial definition joins with its previous version except when its statement
/// always writes the whole low word of the register (a 16-bit local then holds a new value while the
/// upper half stays in the global register). Method entry versions each start a web of their own.
/// </summary>
internal sealed class IrSsaWebs {
    private readonly int[] _webOfVersion;
    private readonly List<int>[] _versionsOfWeb;
    private readonly IrSsaValue[] _valueOfWeb;
    private readonly int _webCount;

    private IrSsaWebs(int[] webOfVersion, List<int>[] versionsOfWeb, IrSsaValue[] valueOfWeb,
        int webCount) {
        _webOfVersion = webOfVersion;
        _versionsOfWeb = versionsOfWeb;
        _valueOfWeb = valueOfWeb;
        _webCount = webCount;
    }

    /// <summary>Builds the webs of <paramref name="ssa"/>.</summary>
    public static IrSsaWebs Build(IrSsa ssa) {
        int count = ssa.VersionCount;
        int[] parent = new int[count];
        for (int i = 0; i < count; i++) {
            parent[i] = i;
        }
        UnionPhiOperands(ssa, parent);
        UnionPartialVersions(ssa, parent);
        return CollectWebs(ssa, parent);
    }

    private static void UnionPhiOperands(IrSsa ssa, int[] parent) {
        foreach (IrSsaPhi phi in ssa.Phis) {
            foreach ((IrBlock _, int version) in phi.Operands) {
                Union(parent, phi.Result, version);
            }
        }
    }

    private static void UnionPartialVersions(IrSsa ssa, int[] parent) {
        Dictionary<int, IrEffects> definingEffects = DefiningEffects(ssa);
        for (int v = 0; v < ssa.VersionCount; v++) {
            IrSsaVersionInfo info = ssa.Info(v);
            if (info.Kind == IrSsaDefinitionKind.PartialDefinition
                && info.Previous >= 0
                && !CutsWeb(v, info, definingEffects)) {
                Union(parent, v, info.Previous);
            }
        }
    }

    private static IrSsaWebs CollectWebs(IrSsa ssa, int[] parent) {
        int count = ssa.VersionCount;
        int[] webOfRoot = new int[count];
        Array.Fill(webOfRoot, -1);
        int[] webOfVersion = new int[count];
        List<int>[] versionsOfWeb = new List<int>[count];
        IrSsaValue[] valueOfWeb = new IrSsaValue[count];
        int webCount = 0;
        for (int v = 0; v < count; v++) {
            int root = Find(parent, v);
            if (webOfRoot[root] < 0) {
                webOfRoot[root] = webCount;
                versionsOfWeb[webCount] = [];
                valueOfWeb[webCount] = ssa.Info(v).Value;
                webCount++;
            }
            int web = webOfRoot[root];
            webOfVersion[v] = web;
            versionsOfWeb[web].Add(v);
        }
        return new IrSsaWebs(webOfVersion, versionsOfWeb, valueOfWeb, webCount);
    }

    /// <summary>The dense id of the web <paramref name="version"/> belongs to (0 to <see cref="WebCount"/> - 1, numbered in order of the smallest version id of each web).</summary>
    public int WebOf(int version) => _webOfVersion[version];

    /// <summary>How many webs exist.</summary>
    public int WebCount => _webCount;

    /// <summary>The versions of web <paramref name="web"/>, ascending ids.</summary>
    public IReadOnlyList<int> VersionsOf(int web) => _versionsOfWeb[web];

    /// <summary>The machine value of web <paramref name="web"/>.</summary>
    public IrSsaValue ValueOf(int web) => _valueOfWeb[web];

    /// <summary>The effects of the site defining each version, found through the sites so a statement a
    /// pass moved with <see cref="IrSsa.Replace(IrStatement, IrStatement)"/> still maps to its versions.</summary>
    private static Dictionary<int, IrEffects> DefiningEffects(IrSsa ssa) {
        Dictionary<int, IrEffects> effects = [];
        HashSet<IrBlock> blocks = [];
        foreach (IrSsaVersionInfo info in Enumerable.Range(0, ssa.VersionCount).Select(ssa.Info)) {
            blocks.Add(info.Block);
        }
        foreach (IrBlock block in blocks) {
            foreach (IrStatement statement in block.Statements) {
                foreach ((IrSsaValue _, int version) in ssa.Definitions(statement)) {
                    effects[version] = statement.Effects;
                }
            }
            foreach ((IrSsaValue _, int version) in ssa.Definitions(block.Terminator)) {
                effects[version] = block.Terminator.Effects;
            }
        }
        return effects;
    }

    /// <summary>A partial definition of a general register cuts the web when its statement always
    /// writes the register's whole low word: the new version then owes nothing to the bytes the
    /// previous version contributed there.</summary>
    private static bool CutsWeb(int version, IrSsaVersionInfo info,
        Dictionary<int, IrEffects> definingEffects) {
        return definingEffects.TryGetValue(version, out IrEffects effects)
            && AlwaysWritesLowWord(info.Value, effects);
    }

    /// <summary>Whether <paramref name="effects"/> always write the whole low word of the general
    /// register <paramref name="value"/> (false for any other machine value).</summary>
    private static bool AlwaysWritesLowWord(IrSsaValue value, IrEffects effects) {
        if (value > IrSsaValue.Edi) {
            return false;
        }
        IrValues lowWord = (IrValues)(0x3UL << (4 * (int)value));
        return (effects.MustWrite & lowWord) == lowWord;
    }

    private static void Union(int[] parent, int first, int second) {
        int rootFirst = Find(parent, first);
        int rootSecond = Find(parent, second);
        if (rootFirst != rootSecond) {
            parent[rootSecond] = rootFirst;
        }
    }

    private static int Find(int[] parent, int version) {
        while (parent[version] != version) {
            parent[version] = parent[parent[version]];
            version = parent[version];
        }
        return version;
    }
}
