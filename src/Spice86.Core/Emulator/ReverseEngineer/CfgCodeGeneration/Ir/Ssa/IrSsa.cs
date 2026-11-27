namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using System.Linq;

/// <summary>
/// Sparse SSA over the values of an <see cref="IrMethod"/>: an analysis-only side table recording a
/// version per definition and per use of every <see cref="IrSsaValue"/>, built from the stored
/// statement and terminator effects. Classic Cytron construction: one phi per value at the iterated
/// dominance frontier of the blocks defining it, then a renaming walk of the dominator tree. A
/// statement that may fault uses every value before its writes; a transfer out of the method uses
/// everything. Blocks the dominator tree does not reach are renamed each as a root starting from
/// method entry versions.
/// </summary>
/// <remarks>
/// Statements and terminators enter and leave the site tables only through <see cref="IrEditor"/>:
/// exact local edits add uses derived from the site's effects and reuse or extend the definition
/// versions; anything the editor cannot keep exact marks the SSA stale (<see cref="IsStale"/>) and
/// every query then throws until the pipeline rebuilds it. Liveness can go stale separately
/// (<see cref="IsLivenessStale"/>) and is recovered by <see cref="RecomputeLiveness"/>.
/// </remarks>
internal sealed class IrSsa {
    private const int ValueCount = (int)IrSsaValue.Count;
    private const int NoVersion = -1;

    private readonly List<IrSsaVersionInfo> _infos = [];
    private readonly Dictionary<IrBlock, int[]> _blockEntryVersions = new();
    private readonly Dictionary<IrBlock, int[]> _methodEntryByBlock = new();
    private readonly Dictionary<IrBlock, List<IrSsaPhi>> _phisByBlock = new();
    private readonly List<IrSsaPhi> _phis = [];
    private readonly Dictionary<int, IrSsaPhi> _phiByResultId = new();
    private readonly Dictionary<int, List<IrSsaUse>> _usesByVersionId = new();
    private readonly Dictionary<int, List<IrSsaUse>> _liveUsesByVersionId = new();
    private readonly HashSet<int> _liveJoinVersionIds = [];
    private readonly Dictionary<object, (IrSsaValue Value, int Version)[]> _usesBySite = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, (IrSsaValue Value, int Version)[]> _definitionsBySite = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, object> _definitionSiteByVersion = new();

    private string? _staleReason;
    private string? _livenessStaleReason;

    private IrSsa() {
    }

    /// <summary>Builds the SSA of <paramref name="method"/>.</summary>
    public static IrSsa Build(IrMethod method) {
        BlockGraph<IrBlock> graph = method.ToBlockGraph();
        DominatorTree<IrBlock> tree = DominatorTreeBuilder.BuildFromEntries(graph);
        return Build(graph, tree, DominanceFrontiers.Compute(graph, tree));
    }

    /// <summary>Builds the SSA of a method on a block graph, dominator tree and
    /// dominance frontiers the caller already computed for its current block structure.</summary>
    public static IrSsa Build(BlockGraph<IrBlock> graph,
        DominatorTree<IrBlock> tree, Dictionary<IrBlock, HashSet<IrBlock>> frontiers) {
        IrSsa ssa = new();
        ssa.CreateEntryVersions(graph);
        Dictionary<IrBlock, List<PlannedPhi>> planned = ssa.PlacePhis(graph, frontiers);
        ssa.Rename(graph, tree, planned);
        ssa.MaterializePhis(planned);
        ssa.ComputeLiveJoinVersions();
        ssa.ComputeLiveUses();
        return ssa;
    }

    /// <summary>Whether an edit marked this SSA stale: every query throws the recorded reason.</summary>
    internal bool IsStale => _staleReason is not null;

    /// <summary>Whether an edit made the liveness tables unreliable: the liveness queries throw the
    /// recorded reason until <see cref="RecomputeLiveness"/> runs.</summary>
    internal bool IsLivenessStale => _livenessStaleReason is not null;

    /// <summary>Marks every query of this SSA stale. Called by <see cref="IrEditor"/> only.</summary>
    internal void MarkStale(string reason) => _staleReason ??= reason;

    /// <summary>Marks the liveness tables stale. Called by <see cref="IrEditor"/> only.</summary>
    internal void MarkLivenessStale(string reason) => _livenessStaleReason ??= reason;

    /// <summary>Rebuilds the live join versions and the live use lists from the current use records.</summary>
    internal void RecomputeLiveness() {
        ThrowIfStale();
        _liveJoinVersionIds.Clear();
        _liveUsesByVersionId.Clear();
        _livenessStaleReason = null;
        ComputeLiveJoinVersions();
        ComputeLiveUses();
    }

    private void ThrowIfStale() {
        if (_staleReason is not null) {
            throw new InvalidOperationException($"The SSA is stale: {_staleReason}");
        }
    }

    private void ThrowIfLivenessStale() {
        ThrowIfStale();
        if (_livenessStaleReason is not null) {
            throw new InvalidOperationException($"The SSA liveness is stale: {_livenessStaleReason}");
        }
    }

    /// <summary>How many versions exist (ids run from 0 to one less).</summary>
    public int VersionCount {
        get {
            ThrowIfStale();
            return _infos.Count;
        }
    }

    /// <summary>Every phi of every block.</summary>
    public IReadOnlyList<IrSsaPhi> Phis {
        get {
            ThrowIfStale();
            return _phis;
        }
    }

    /// <summary>What <paramref name="version"/> is.</summary>
    public IrSsaVersionInfo Info(int version) {
        ThrowIfStale();
        return _infos[version];
    }

    /// <summary>The version <paramref name="statement"/> uses of <paramref name="value"/>, or -1 when it does not use the value.</summary>
    public int VersionUsed(IrStatement statement, IrSsaValue value) {
        ThrowIfStale();
        return _usesBySite.TryGetValue(statement, out (IrSsaValue Value, int Version)[]? uses)
            ? VersionOf(uses, value)
            : NoVersion;
    }

    /// <summary>The values <paramref name="statement"/> defines, with their new versions.</summary>
    public IReadOnlyList<(IrSsaValue Value, int Version)> Definitions(IrStatement statement) {
        ThrowIfStale();
        return _definitionsBySite.TryGetValue(statement, out (IrSsaValue Value, int Version)[]? definitions)
            ? definitions
            : [];
    }

    /// <summary>The statement that defines <paramref name="version"/>, or <c>null</c> when its site is
    /// not a still-present statement (terminator, phi, method entry or a dropped definition).</summary>
    internal IrStatement? DefiningStatement(int version) {
        ThrowIfStale();
        if (version < 0) {
            return null;
        }
        return _definitionSiteByVersion.GetValueOrDefault(version) as IrStatement;
    }

    /// <summary>The version <paramref name="terminator"/> uses of <paramref name="value"/>, or -1 when it does not use the value.</summary>
    internal int VersionUsed(IrTerminator terminator, IrSsaValue value) {
        ThrowIfStale();
        return _usesBySite.TryGetValue(terminator, out (IrSsaValue Value, int Version)[]? uses)
            ? VersionOf(uses, value)
            : NoVersion;
    }

    /// <summary>The values <paramref name="terminator"/> defines, with their new versions.</summary>
    internal IReadOnlyList<(IrSsaValue Value, int Version)> Definitions(IrTerminator terminator) {
        ThrowIfStale();
        return _definitionsBySite.TryGetValue(terminator, out (IrSsaValue Value, int Version)[]? definitions)
            ? definitions
            : [];
    }

    /// <summary>
    /// The version of <paramref name="value"/> before the statement at <paramref name="statementIndex"/>
    /// in <paramref name="block"/> runs: the nearest earlier definition in the block, else the block
    /// entry version (a phi result counts as the entry version). An index of
    /// <c>block.Statements.Count</c> means before the terminator. A statement a pass inserted without
    /// the editor has no site here: it is skipped when it defines no machine value, and a defining
    /// statement without a site is a pass bug and throws.
    /// </summary>
    public int VersionBefore(IrBlock block, int statementIndex, IrSsaValue value) {
        ThrowIfStale();
        for (int index = statementIndex - 1; index >= 0; index--) {
            IrStatement statement = block.Statements[index];
            if (!_definitionsBySite.TryGetValue(statement, out (IrSsaValue Value, int Version)[]? definitions)) {
                RequireNonDefining(statement);
                continue;
            }
            int version = VersionOf(definitions, value);
            if (version != NoVersion) {
                return version;
            }
        }
        return _blockEntryVersions[block][(int)value];
    }

    /// <summary>
    /// The version method entry defines for <paramref name="value"/> at
    /// <paramref name="entryBlock"/>: the value the caller passed. Unlike
    /// <see cref="VersionBefore"/> at index 0 of the entry block, a loop-head phi does not
    /// stand in for it.
    /// </summary>
    public int MethodEntryVersion(IrBlock entryBlock, IrSsaValue value) {
        ThrowIfStale();
        return _methodEntryByBlock[entryBlock][(int)value];
    }

    /// <summary>
    /// The version of <paramref name="value"/> after the statement at <paramref name="statementIndex"/>
    /// in <paramref name="block"/> ran. An index of <c>block.Statements.Count</c> means after the
    /// terminator.
    /// </summary>
    public int VersionAfter(IrBlock block, int statementIndex, IrSsaValue value) {
        ThrowIfStale();
        object site = statementIndex == block.Statements.Count ? block.Terminator : block.Statements[statementIndex];
        if (!_definitionsBySite.TryGetValue(site, out (IrSsaValue Value, int Version)[]? siteDefinitions)) {
            RequireNonDefining(site);
            siteDefinitions = [];
        }
        int defined = VersionOf(siteDefinitions, value);
        return defined != NoVersion ? defined : VersionBefore(block, statementIndex, value);
    }

    /// <summary>A site this SSA never recorded must not define a machine value: a pass inserted it
    /// without the editor, so its writes would be invisible to version queries.</summary>
    private static void RequireNonDefining(object site) {
        IrEffects effects = site switch {
            IrStatement statement => statement.Effects,
            IrTerminator terminator => terminator.Effects,
            _ => throw new InvalidOperationException($"Not an SSA site: {site}")
        };
        (uint full, uint partial) = IrSsaValues.WriteBits(effects);
        if ((full | partial) != 0) {
            throw new InvalidOperationException(
                $"A statement that defines machine values was inserted without an SSA site: {site}");
        }
    }

    /// <summary>The statements, terminators and phis reading <paramref name="version"/>.</summary>
    public IReadOnlyList<IrSsaUse> Uses(int version) {
        ThrowIfStale();
        return _usesByVersionId.TryGetValue(version, out List<IrSsaUse>? uses) ? uses : [];
    }

    /// <summary>
    /// The uses of <paramref name="version"/> minus the dead ones: a phi is dead when its result
    /// reaches no statement or terminator, and the use a partial definition makes of the previous
    /// version is dead when the new version reaches no use.
    /// </summary>
    public IReadOnlyList<IrSsaUse> LiveUses(int version) {
        ThrowIfLivenessStale();
        return _liveUsesByVersionId.TryGetValue(version, out List<IrSsaUse>? uses) ? uses : [];
    }

    /// <summary>Whether <paramref name="version"/> reaches a statement or terminator.</summary>
    public bool IsLive(int version) => LiveUses(version).Count != 0;

    /// <summary>
    /// Whether every byte of <paramref name="bytes"/> that <paramref name="version"/> contributes
    /// is consumed only by <paramref name="allowed"/>. The worklist follows the byte mask through
    /// phi results and through partial definitions (conditional or explicit): a phi passes the mask
    /// to its result version, a site that faults or reads a followed byte refuses, and a partial
    /// definition carries the bytes it does not overwrite into its new version - a full overwrite
    /// ends the tracking for those bytes. Sites a pass removed no longer sit in the SSA and are
    /// skipped; any other live use refuses.
    /// </summary>
    internal bool IsUsedOnlyAt(int version, CpuValues bytes, IrTerminator allowed) {
        ThrowIfLivenessStale();
        List<(int Version, CpuValues Bytes)> worklist = [(version, bytes)];
        HashSet<(int Version, CpuValues Bytes)> visited = [];
        while (worklist.Count > 0) {
            (int current, CpuValues mask) = worklist[worklist.Count - 1];
            worklist.RemoveAt(worklist.Count - 1);
            if (!visited.Add((current, mask))) {
                continue;
            }
            foreach (IrSsaUse use in LiveUses(current)) {
                if (use.Phi is { } phi) {
                    worklist.Add((phi.Result, mask));
                    continue;
                }
                object? site = use.Statement ?? (object?)use.Terminator;
                if (site is null) {
                    return false;
                }
                if (!_usesBySite.ContainsKey(site)) {
                    continue;
                }
                if (ReferenceEquals(site, allowed)) {
                    continue;
                }
                if (site is not (IrStatement or IrTerminator)) {
                    return false;
                }
                IrEffects effects = site is IrStatement statement
                    ? statement.Effects : ((IrTerminator)site).Effects;
                if (effects.MayFault || (effects.Reads & mask) != CpuValues.None) {
                    return false;
                }
                if (_definitionsBySite.TryGetValue(site,
                        out (IrSsaValue Value, int Version)[]? definitions)) {
                    foreach ((IrSsaValue _, int newVersion) in definitions) {
                        CpuValues surviving = mask & ~effects.MustWrite;
                        if (_infos[newVersion].Previous == current && surviving != CpuValues.None) {
                            worklist.Add((newVersion, surviving));
                        }
                    }
                }
            }
        }
        return true;
    }

    /// <summary>The recorded uses of every site, for the fresh-build comparison.</summary>
    internal IReadOnlyDictionary<object, (IrSsaValue Value, int Version)[]> UsesBySite {
        get {
            ThrowIfStale();
            return _usesBySite;
        }
    }

    /// <summary>The recorded definitions of every site, for the fresh-build comparison.</summary>
    internal IReadOnlyDictionary<object, (IrSsaValue Value, int Version)[]> DefinitionsBySite {
        get {
            ThrowIfStale();
            return _definitionsBySite;
        }
    }

    /// <summary>The statement or terminator that defines <paramref name="version"/>, or null for a phi,
    /// a method entry version or a dropped definition.</summary>
    internal object? DefinitionSite(int version) {
        ThrowIfStale();
        return _definitionSiteByVersion.GetValueOrDefault(version);
    }

    /// <summary>The phi whose result is <paramref name="version"/>, or <c>null</c>.</summary>
    internal IrSsaPhi? PhiDefining(int version) => _phiByResultId.GetValueOrDefault(version);

    /// <summary>The phis at the top of <paramref name="block"/>.</summary>
    public IReadOnlyList<IrSsaPhi> PhisOf(IrBlock block) {
        ThrowIfStale();
        return _phisByBlock.TryGetValue(block, out List<IrSsaPhi>? phis) ? phis : [];
    }

    /// <summary>
    /// Whether every value covered by <paramref name="reads"/> and <paramref name="tokenReads"/> has the
    /// same version before position <paramref name="from"/> and before position <paramref name="to"/>:
    /// the legality rule for moving or merging an expression between the two places.
    /// </summary>
    public bool SameVersions(CpuValues reads, IrTokens tokenReads, (IrBlock Block, int Index) from,
        (IrBlock Block, int Index) to) {
        uint values = IrSsaValues.ValueBits(reads) | IrSsaValues.TokenBits(tokenReads);
        for (int v = 0; v < ValueCount; v++) {
            if ((values & (1u << v)) == 0) {
                continue;
            }
            if (VersionBefore(from.Block, from.Index, (IrSsaValue)v)
                != VersionBefore(to.Block, to.Index, (IrSsaValue)v)) {
                return false;
            }
        }
        return true;
    }

    /// <summary>The position of <paramref name="site"/> in <paramref name="block"/>: the index of a
    /// statement or the statement count for the terminator; -1 for a site no longer in the block.</summary>
    internal static int PositionIn(IrBlock block, object site) {
        if (site is IrTerminator) {
            return block.Statements.Count;
        }
        if (site is IrStatement statement) {
            return block.IndexOfStatement(statement);
        }
        return -1;
    }

    /// <summary>The values the site of <paramref name="effects"/> uses: its reads, its token reads,
    /// everything when it may fault, and the values it partially writes. Shared by the builder's
    /// renaming and the editor's derived uses.</summary>
    internal static uint UsedBits(IrEffects effects) {
        uint reads = IrSsaValues.ValueBits(effects.Reads) | IrSsaValues.TokenBits(effects.TokenReads);
        if (effects.MayFault) {
            reads |= effects.FaultReads is { } faultReads
                ? IrSsaValues.ValueBits(faultReads)
                : IrSsaValues.All;
        }
        (uint _, uint partial) = IrSsaValues.WriteBits(effects);
        return reads | partial;
    }

    /// <summary>The values the site of <paramref name="effects"/> reads unconditionally: its reads and
    /// token reads, or everything when it may fault. Excludes the partial writes, which read the
    /// previous version only while their new version is live.</summary>
    internal static uint ReadBits(IrEffects effects) {
        uint reads = IrSsaValues.ValueBits(effects.Reads) | IrSsaValues.TokenBits(effects.TokenReads);
        if (effects.MayFault) {
            reads |= effects.FaultReads is { } faultReads
                ? IrSsaValues.ValueBits(faultReads)
                : IrSsaValues.All;
        }
        return reads;
    }

    /// <summary>
    /// Drops <paramref name="site"/> from the site tables: its use records leave both use lists and its
    /// definition-site entries are removed, but the versions it defined keep their records. Returns the
    /// definitions the site held. Deleting a live use of a join version marks the liveness stale.
    /// Called by <see cref="IrEditor"/> only.
    /// </summary>
    internal (IrSsaValue Value, int Version)[] RemoveSiteRecords(object site) {
        _usesBySite.Remove(site, out (IrSsaValue Value, int Version)[]? uses);
        _definitionsBySite.Remove(site, out (IrSsaValue Value, int Version)[]? definitions);
        if (uses is not null) {
            foreach ((IrSsaValue _, int version) in uses) {
                RemoveUseRecords(version, site);
            }
        }
        if (definitions is not null) {
            foreach ((IrSsaValue _, int version) in definitions) {
                _definitionSiteByVersion.Remove(version);
            }
        }
        MarkLivenessStale($"site {SiteName(site)} was edited");
        return definitions ?? [];
    }

    /// <summary>Deletes the use records <paramref name="site"/> held on <paramref name="version"/> from
    /// the recorded and the live use lists.</summary>
    private void RemoveUseRecords(int version, object site) {
        if (_usesByVersionId.TryGetValue(version, out List<IrSsaUse>? uses)) {
            uses.RemoveAll(use => References(use, site));
        }
        if (_liveUsesByVersionId.TryGetValue(version, out List<IrSsaUse>? live)) {
            int removed = live.RemoveAll(use => References(use, site));
            if (removed != 0
                && _infos[version].Kind is IrSsaDefinitionKind.Phi or IrSsaDefinitionKind.PartialDefinition) {
                MarkLivenessStale($"a live use of {_infos[version].Value} was removed from a join version");
            }
        }
    }

    private static bool References(IrSsaUse use, object site) =>
        ReferenceEquals(use.Statement, site) || ReferenceEquals(use.Terminator, site);

    /// <summary>
    /// Records <paramref name="site"/> at position <paramref name="index"/> of <paramref name="block"/>
    /// with <paramref name="uses"/> derived from its effects and <paramref name="definitions"/> assigned
    /// by the edit rules. Each use is appended to the recorded uses of its version; a live use is also
    /// appended to the live list, except that feeding a join version outside the live join set marks the
    /// liveness stale. Called by <see cref="IrEditor"/> only.
    /// </summary>
    internal void RecordSite(IrBlock block, object site,
        List<(IrSsaValue Value, int Version, int ConditionalNewVersion)> uses,
        List<(IrSsaValue Value, int Version)> definitions) {
        _usesBySite[site] = uses
            .Select(use => (use.Value, use.Version))
            .ToArray();
        _definitionsBySite[site] = [.. definitions];
        foreach ((IrSsaValue _, int version) in definitions) {
            _definitionSiteByVersion[version] = site;
        }
        IrStatement? statement = site as IrStatement;
        IrTerminator? terminator = site as IrTerminator;
        foreach ((IrSsaValue _, int version, int conditionalNewVersion) in uses) {
            IrSsaUse use = new(block, statement, terminator, null, null) {
                ConditionalNewVersion = conditionalNewVersion
            };
            AddUse(version, use);
            AddLiveUse(version, use);
        }
        MarkLivenessStale($"site {SiteName(site)} was edited");
    }

    private static string SiteName(object site) => site switch {
        IrStatement statement => statement.Node.GetType().Name,
        IrTerminator terminator => terminator.GetType().Name,
        _ => site.GetType().Name
    };

    /// <summary>Appends <paramref name="use"/> to the live list of <paramref name="version"/> when the
    /// use is live: an unconditional use is live, a conditional one lives only while the new join
    /// version does. A live use of a plain definition or method entry version always joins the live
    /// list; one of a join version does so only while the join is live, else the liveness is stale.</summary>
    private void AddLiveUse(int version, IrSsaUse use) {
        bool join = _infos[version].Kind is IrSsaDefinitionKind.Phi or IrSsaDefinitionKind.PartialDefinition;
        if (join && !_liveJoinVersionIds.Contains(version)) {
            MarkLivenessStale($"a use was added to the dead join version {_infos[version].Value}");
            return;
        }
        bool live = use.ConditionalNewVersion < 0
            || _liveJoinVersionIds.Contains(use.ConditionalNewVersion);
        if (live) {
            if (!_liveUsesByVersionId.TryGetValue(version, out List<IrSsaUse>? liveUses)) {
                liveUses = [];
                _liveUsesByVersionId[version] = liveUses;
            }
            liveUses.Add(use);
        }
    }

    /// <summary>A fresh full-definition version of <paramref name="value"/> at
    /// <paramref name="site"/> in <paramref name="block"/>. Called by <see cref="IrEditor"/> only.</summary>
    internal int NewDefinition(IrBlock block, IrSsaValue value, object site) {
        int version = NewVersion(value, IrSsaDefinitionKind.Definition, block, -1);
        _definitionSiteByVersion[version] = site;
        return version;
    }

    /// <summary>Drops the definition record of <paramref name="version"/>: the version keeps its info
    /// but no site defines it anymore. Called by <see cref="IrEditor"/> only.</summary>
    internal void DropDefinition(int version) => _definitionSiteByVersion.Remove(version);

    /// <summary>Whether <paramref name="version"/> still has a recorded use. Called by
    /// <see cref="IrEditor"/> only.</summary>
    internal bool HasAnyUse(int version) =>
        _usesByVersionId.TryGetValue(version, out List<IrSsaUse>? uses) && uses.Count != 0;

    private static int VersionOf((IrSsaValue Value, int Version)[] pairs, IrSsaValue value) {
        foreach ((IrSsaValue Value, int Version) pair in pairs) {
            if (pair.Value == value) {
                return pair.Version;
            }
        }
        return NoVersion;
    }

    private void CreateEntryVersions(BlockGraph<IrBlock> graph) {
        foreach (IrBlock entry in graph.EntryBlocks) {
            _methodEntryByBlock[entry] = NewMethodEntryVersions(entry);
        }
    }

    private int[] NewMethodEntryVersions(IrBlock block) {
        int[] versions = new int[ValueCount];
        for (int v = 0; v < ValueCount; v++) {
            versions[v] = NewVersion((IrSsaValue)v, IrSsaDefinitionKind.MethodEntry, block, -1);
        }
        return versions;
    }

    private int[] EntryVersionsFor(IrBlock block) {
        if (!_methodEntryByBlock.TryGetValue(block, out int[]? versions)) {
            versions = NewMethodEntryVersions(block);
            _methodEntryByBlock[block] = versions;
        }
        return versions;
    }

    private static uint DefinitionsOf(IrEffects effects) {
        (uint full, uint partial) = IrSsaValues.WriteBits(effects);
        return full | partial;
    }

    private Dictionary<IrBlock, List<PlannedPhi>> PlacePhis(
        BlockGraph<IrBlock> graph,
        Dictionary<IrBlock, HashSet<IrBlock>> frontiers) {
        HashSet<IrBlock>[] defsites = new HashSet<IrBlock>[ValueCount];
        for (int v = 0; v < ValueCount; v++) {
            defsites[v] = [];
        }
        foreach (IrBlock block in graph.Blocks) {
            uint defined = 0;
            foreach (IrStatement statement in block.Statements) {
                defined |= DefinitionsOf(statement.Effects);
            }
            defined |= DefinitionsOf(block.Terminator.Effects);
            for (int v = 0; v < ValueCount; v++) {
                if ((defined & (1u << v)) != 0) {
                    defsites[v].Add(block);
                }
            }
        }

        HashSet<IrBlock> entries = graph.EntryBlocks.ToHashSet();
        Dictionary<IrBlock, List<PlannedPhi>> planned = new();
        Dictionary<IrBlock, bool[]> plannedValues = new();
        Dictionary<IrBlock, List<IrBlock>> sortedPredecessors = new();
        for (int v = 0; v < ValueCount; v++) {
            List<IrBlock> worklist = [.. defsites[v]];
            while (worklist.Count > 0) {
                IrBlock defining = worklist[worklist.Count - 1];
                worklist.RemoveAt(worklist.Count - 1);
                foreach (IrBlock frontier in frontiers[defining]) {
                    if (plannedValues.TryGetValue(frontier, out bool[]? values) && values[v]) {
                        continue;
                    }
                    AddPlannedPhi(planned, plannedValues, entries, sortedPredecessors, graph,
                        frontier, (IrSsaValue)v);
                    if (!defsites[v].Contains(frontier)) {
                        worklist.Add(frontier);
                    }
                }
            }
        }
        return planned;
    }

    private static void AddPlannedPhi(
        Dictionary<IrBlock, List<PlannedPhi>> planned,
        Dictionary<IrBlock, bool[]> plannedValues,
        HashSet<IrBlock> entries,
        Dictionary<IrBlock, List<IrBlock>> sortedPredecessors,
        BlockGraph<IrBlock> graph,
        IrBlock block,
        IrSsaValue value) {
        if (!planned.TryGetValue(block, out List<PlannedPhi>? plans)) {
            plans = [];
            planned[block] = plans;
        }
        if (!plannedValues.TryGetValue(block, out bool[]? values)) {
            values = new bool[ValueCount];
            plannedValues[block] = values;
        }
        values[(int)value] = true;
        if (!sortedPredecessors.TryGetValue(block, out List<IrBlock>? blockPredecessors)) {
            blockPredecessors = [.. graph.PredecessorsByBlock[block]];
            blockPredecessors.Sort(graph.StableOrder);
            sortedPredecessors[block] = blockPredecessors;
        }
        PlannedPhi plan = new(value, blockPredecessors);
        plan.HasEntryOperand = entries.Contains(block);
        plans.Add(plan);
    }

    private void Rename(
        BlockGraph<IrBlock> graph,
        DominatorTree<IrBlock> tree,
        Dictionary<IrBlock, List<PlannedPhi>> plannedPhis) {
        HashSet<IrBlock> reachable = tree.Blocks.ToHashSet();
        Dictionary<IrBlock, List<IrBlock>> children = tree.Blocks
            .ToDictionary(block => block, _ => new List<IrBlock>());
        List<IrBlock> roots = [];
        foreach (IrBlock block in tree.Blocks) {
            IrBlock? parent = tree.GetImmediateDominator(block);
            if (parent is null) {
                roots.Add(block);
            } else {
                children[parent].Add(block);
            }
        }
        roots.Sort(graph.StableOrder);
        foreach (List<IrBlock> list in children.Values) {
            list.Sort(graph.StableOrder);
        }
        List<IrBlock> unreachable = graph.Blocks
            .Where(block => !reachable.Contains(block))
            .OrderBy(block => block, graph.StableOrder)
            .ToList();
        List<(IrBlock Block, int[] Current)> stack = [];
        for (int i = unreachable.Count - 1; i >= 0; i--) {
            stack.Add((unreachable[i], (int[])EntryVersionsFor(unreachable[i]).Clone()));
        }
        for (int i = roots.Count - 1; i >= 0; i--) {
            stack.Add((roots[i], (int[])EntryVersionsFor(roots[i]).Clone()));
        }
        while (stack.Count > 0) {
            (IrBlock block, int[] current) = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            RenameBlock(plannedPhis, block, current);
            if (children.TryGetValue(block, out List<IrBlock>? dominatorChildren)) {
                for (int i = dominatorChildren.Count - 1; i >= 0; i--) {
                    stack.Add((dominatorChildren[i], (int[])current.Clone()));
                }
            }
        }
    }

    private void RenameBlock(
        Dictionary<IrBlock, List<PlannedPhi>> plannedPhis,
        IrBlock block,
        int[] current) {
        if (plannedPhis.TryGetValue(block, out List<PlannedPhi>? plans)) {
            foreach (PlannedPhi plan in plans) {
                plan.Result = NewVersion(plan.Value, IrSsaDefinitionKind.Phi, block, -1);
                current[(int)plan.Value] = plan.Result;
            }
        }
        _blockEntryVersions[block] = (int[])current.Clone();
        for (int index = 0; index < block.Statements.Count; index++) {
            RenamePosition(block.Statements[index].Effects, block, index, current,
                block.Statements[index], null);
        }
        RenamePosition(block.Terminator.Effects, block, block.Statements.Count, current,
            null, block.Terminator);
        foreach (IrEdge edge in block.Terminator.Edges) {
            if (!plannedPhis.TryGetValue(edge.Target, out List<PlannedPhi>? successorPlans)) {
                continue;
            }
            foreach (PlannedPhi plan in successorPlans) {
                if (!plan.SlotByPredecessor.TryGetValue(block, out int slot)) {
                    continue;
                }
                plan.OperandVersions[slot] = current[(int)plan.Value];
            }
        }
    }

    private void RenamePosition(IrEffects effects, IrBlock block, int index, int[] current,
        IrStatement? statement, IrTerminator? terminator) {
        uint reads = ReadBits(effects);
        (uint full, uint partial) = IrSsaValues.WriteBits(effects);
        uint used = reads | partial;
        List<(IrSsaValue Value, int Version)> uses = [];
        for (int v = 0; v < ValueCount; v++) {
            if ((used & (1u << v)) == 0) {
                continue;
            }
            uses.Add(((IrSsaValue)v, current[v]));
        }
        object site = (object?)statement ?? terminator
            ?? throw new InvalidOperationException("A renamed position needs a statement or a terminator.");
        _usesBySite[site] = [.. uses];
        List<(IrSsaValue Value, int Version)> definitions = [];
        for (int v = 0; v < ValueCount; v++) {
            if ((full & (1u << v)) != 0) {
                current[v] = NewVersion((IrSsaValue)v, IrSsaDefinitionKind.Definition, block, -1);
                definitions.Add(((IrSsaValue)v, current[v]));
            }
        }
        for (int v = 0; v < ValueCount; v++) {
            if ((partial & (1u << v)) == 0) {
                continue;
            }
            int previous = current[v];
            int fresh = NewVersion((IrSsaValue)v, IrSsaDefinitionKind.PartialDefinition, block, previous);
            current[v] = fresh;
            definitions.Add(((IrSsaValue)v, fresh));
            bool conditional = (reads & (1u << v)) == 0;
            AddUse(previous, new IrSsaUse(block, statement, terminator, null, null) {
                ConditionalNewVersion = conditional ? fresh : -1
            });
        }
        foreach ((IrSsaValue usedValue, int usedVersion) in uses) {
            if ((partial & (1u << (int)usedValue)) != 0) {
                continue;
            }
            AddUse(usedVersion, new IrSsaUse(block, statement, terminator, null, null));
        }
        _definitionsBySite[site] = [.. definitions];
        foreach ((IrSsaValue _, int definedVersion) in definitions) {
            _definitionSiteByVersion[definedVersion] = site;
        }
    }

    private void MaterializePhis(Dictionary<IrBlock, List<PlannedPhi>> plannedPhis) {
        foreach ((IrBlock block, List<PlannedPhi> plans) in plannedPhis) {
            List<IrSsaPhi> phis = [];
            _phisByBlock[block] = phis;
            int[]? entryVersions = _methodEntryByBlock.GetValueOrDefault(block);
            foreach (PlannedPhi plan in plans) {
                List<(IrBlock Predecessor, int Version)> operands = [];
                for (int i = 0; i < plan.Predecessors.Count; i++) {
                    operands.Add((plan.Predecessors[i], plan.OperandVersions[i]));
                }
                IrSsaPhi phi = new(plan.Value, plan.Result, operands, plan.HasEntryOperand);
                phis.Add(phi);
                _phis.Add(phi);
                _phiByResultId[phi.Result] = phi;
                foreach ((IrBlock predecessor, int version) in operands) {
                    AddUse(version, new IrSsaUse(block, null, null, phi, predecessor));
                }
                if (plan.HasEntryOperand && entryVersions is not null) {
                    AddUse(entryVersions[(int)plan.Value],
                        new IrSsaUse(block, null, null, phi, block));
                }
            }
            phis.Sort((first, second) => first.Value.CompareTo(second.Value));
        }
    }

    /// <summary>Marks the join versions (phi results and partial definitions) whose value still reaches a
    /// statement or terminator. A join version is live when it has a live use: an unconditional statement
    /// or terminator use is live; a phi operand use is live only when the phi is; the use a partial
    /// definition makes of the previous version is live only when the new version is.</summary>
    private void ComputeLiveJoinVersions() {
        List<int> worklist = [];
        for (int id = 0; id < _infos.Count; id++) {
            if (_infos[id].Kind is not (IrSsaDefinitionKind.Phi or IrSsaDefinitionKind.PartialDefinition)) {
                continue;
            }
            if (Uses(id).Any(use => use.Phi is null && use.ConditionalNewVersion == -1)) {
                _liveJoinVersionIds.Add(id);
                worklist.Add(id);
            }
        }
        while (worklist.Count > 0) {
            int live = worklist[worklist.Count - 1];
            worklist.RemoveAt(worklist.Count - 1);
            foreach (int usedId in ConditionallyUsedIds(live)) {
                if (_infos[usedId].Kind is not (IrSsaDefinitionKind.Phi or IrSsaDefinitionKind.PartialDefinition)
                    || !_liveJoinVersionIds.Add(usedId)) {
                    continue;
                }
                worklist.Add(usedId);
            }
        }
    }

    private void ComputeLiveUses() {
        for (int id = 0; id < _infos.Count; id++) {
            if (!_usesByVersionId.TryGetValue(id, out List<IrSsaUse>? uses)) {
                continue;
            }
            List<IrSsaUse> live = new(uses.Count);
            foreach (IrSsaUse use in uses.Where(IsLiveUse)) {
                live.Add(use);
            }
            _liveUsesByVersionId[id] = live;
        }
    }

    private bool IsLiveUse(IrSsaUse use) {
        if (use.Phi is not null) {
            return _liveJoinVersionIds.Contains(use.Phi.Result);
        }
        return use.ConditionalNewVersion == -1 || _liveJoinVersionIds.Contains(use.ConditionalNewVersion);
    }

    private IEnumerable<int> ConditionallyUsedIds(int versionId) {
        if (_phiByResultId.TryGetValue(versionId, out IrSsaPhi? phi)) {
            return phi.Operands.Select(operand => operand.Version);
        }
        int previous = _infos[versionId].Previous;
        return previous >= 0 ? [previous] : [];
    }

    private int NewVersion(IrSsaValue value, IrSsaDefinitionKind kind, IrBlock block, int previous) {
        int version = _infos.Count;
        _infos.Add(new IrSsaVersionInfo(value, kind, block, previous));
        return version;
    }

    private void AddUse(int version, IrSsaUse use) {
        if (!_usesByVersionId.TryGetValue(version, out List<IrSsaUse>? uses)) {
            uses = [];
            _usesByVersionId[version] = uses;
        }
        uses.Add(use);
    }

    private sealed class PlannedPhi {
        public PlannedPhi(IrSsaValue value, List<IrBlock> predecessors) {
            Value = value;
            Predecessors = predecessors;
            OperandVersions = new int[predecessors.Count];
            for (int i = 0; i < predecessors.Count; i++) {
                SlotByPredecessor[predecessors[i]] = i;
            }
        }

        public IrSsaValue Value { get; }
        public List<IrBlock> Predecessors { get; }
        public int[] OperandVersions { get; }
        public Dictionary<IrBlock, int> SlotByPredecessor { get; } = new();
        public bool HasEntryOperand { get; set; }
        public int Result { get; set; }
    }
}
