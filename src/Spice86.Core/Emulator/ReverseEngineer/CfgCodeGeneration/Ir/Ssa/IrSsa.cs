namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

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

    /// <summary>How many versions exist (ids run from 0 to one less).</summary>
    public int VersionCount => _infos.Count;

    /// <summary>Every phi of every block.</summary>
    public IReadOnlyList<IrSsaPhi> Phis => _phis;

    /// <summary>What <paramref name="version"/> is.</summary>
    public IrSsaVersionInfo Info(int version) => _infos[version];

    /// <summary>The version <paramref name="statement"/> uses of <paramref name="value"/>, or -1 when it does not use the value.</summary>
    public int VersionUsed(IrStatement statement, IrSsaValue value) =>
        _usesBySite.TryGetValue(statement, out (IrSsaValue Value, int Version)[]? uses)
            ? VersionOf(uses, value)
            : NoVersion;

    /// <summary>The values <paramref name="statement"/> defines, with their new versions.</summary>
    public IReadOnlyList<(IrSsaValue Value, int Version)> Definitions(IrStatement statement) =>
        _definitionsBySite.TryGetValue(statement, out (IrSsaValue Value, int Version)[]? definitions)
            ? definitions
            : [];

    /// <summary>Whether a site of this SSA still defines <paramref name="version"/> of
    /// <paramref name="value"/>: <see cref="Remove"/> and <see cref="Replace(IrStatement, IrStatement)"/>
    /// can leave a version recorded as the previous link of a partial definition while no position
    /// defines it anymore.</summary>
    internal bool HasDefinitionSite(IrSsaValue value, int version) =>
        _definitionsBySite.Values.Any(definitions =>
            definitions.Any(definition => definition.Value == value && definition.Version == version));

    /// <summary>The version <paramref name="terminator"/> uses of <paramref name="value"/>, or -1 when it does not use the value.</summary>
    internal int VersionUsed(IrTerminator terminator, IrSsaValue value) =>
        _usesBySite.TryGetValue(terminator, out (IrSsaValue Value, int Version)[]? uses)
            ? VersionOf(uses, value)
            : NoVersion;

    /// <summary>
    /// Distributes the site of <paramref name="old"/> over <paramref name="replacements"/>: every
    /// replacement gets the recorded uses, and each old definition goes to the last replacement
    /// whose writes include the defined value - or to the last replacement when none writes it.
    /// Replacements that get no definition keep an empty site: version queries skip it, and a
    /// replacement left site-less would be read as a statement a pass inserted without a rebuild.
    /// Every machine value the replacements define must already be defined by
    /// <paramref name="old"/>; otherwise the statement cannot be rebuilt under this SSA and the call
    /// throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public void Replace(IrStatement old, IrStatement replacement) => Replace(old, [replacement]);

    /// <inheritdoc cref="Replace(IrStatement, IrStatement)"/>
    public void Replace(IrStatement old, IReadOnlyList<IrStatement> replacements) {
        if (!_definitionsBySite.Remove(old, out (IrSsaValue Value, int Version)[]? definitions)
            || !_usesBySite.Remove(old, out (IrSsaValue Value, int Version)[]? uses)) {
            throw new InvalidOperationException(
                $"The replaced statement is not a site of this SSA: {old.Node.GetType().Name} at {old.Origin.Address}");
        }
        uint oldDefined = 0;
        foreach ((IrSsaValue value, int _) in definitions) {
            oldDefined |= 1u << (int)value;
        }
        uint[] replacementWrites = new uint[replacements.Count];
        uint newDefined = 0;
        for (int i = 0; i < replacements.Count; i++) {
            (uint full, uint partial) = IrSsaValues.WriteBits(replacements[i].Effects);
            replacementWrites[i] = full | partial;
            newDefined |= full | partial;
        }
        if ((newDefined & ~oldDefined) != 0) {
            throw new InvalidOperationException(
                $"The replacement defines a machine value the replaced statement did not: {old.Node.GetType().Name} at {old.Origin.Address}");
        }
        if (replacements.Count == 0) {
            return;
        }
        List<(IrSsaValue Value, int Version)>[] definitionsByReplacement =
            new List<(IrSsaValue Value, int Version)>[replacements.Count];
        for (int i = 0; i < replacements.Count; i++) {
            definitionsByReplacement[i] = [];
        }
        foreach ((IrSsaValue value, int version) in definitions) {
            int target = replacements.Count - 1;
            for (int i = replacements.Count - 1; i >= 0; i--) {
                if ((replacementWrites[i] & (1u << (int)value)) != 0) {
                    target = i;
                    break;
                }
            }
            definitionsByReplacement[target].Add((value, version));
        }
        // Every replacement gets the recorded uses: a replacement left site-less would be read as a
        // statement a pass inserted without an SSA rebuild.
        for (int i = 0; i < replacements.Count; i++) {
            _definitionsBySite[replacements[i]] = [.. definitionsByReplacement[i]];
            _usesBySite[replacements[i]] = uses;
        }
    }

    /// <summary>Whether <paramref name="statement"/> was present when this SSA was built, so a pass
    /// knows it can call <see cref="Replace(IrStatement, IrStatement)"/> or <see cref="Remove"/> on it.</summary>
    public bool HasSite(IrStatement statement) => _usesBySite.ContainsKey(statement);

    /// <summary>
    /// Drops the site of <paramref name="statement"/>: its definitions stay recorded as versions but
    /// no position maps to them. Only allowed on a statement whose definitions have no live use.
    /// </summary>
    public void Remove(IrStatement statement) {
        _definitionsBySite.Remove(statement);
        _usesBySite.Remove(statement);
    }

    /// <summary>Moves the site of a replaced terminator: <paramref name="replacement"/> gets the
    /// recorded uses and definitions. Every machine value it defines must already be defined by
    /// <paramref name="old"/>; otherwise the terminator cannot be rebuilt under this SSA and the
    /// call throws like <see cref="Replace(IrStatement, IrStatement)"/>.</summary>
    internal void ReplaceTerminator(IrTerminator old, IrTerminator replacement) {
        if (!_definitionsBySite.Remove(old, out (IrSsaValue Value, int Version)[]? definitions)
            || !_usesBySite.Remove(old, out (IrSsaValue Value, int Version)[]? uses)) {
            throw new InvalidOperationException("The replaced terminator is not a site of this SSA.");
        }
        uint oldDefined = 0;
        foreach ((IrSsaValue value, int _) in definitions) {
            oldDefined |= 1u << (int)value;
        }
        (uint full, uint partial) = IrSsaValues.WriteBits(replacement.Effects);
        if (((full | partial) & ~oldDefined) != 0) {
            throw new InvalidOperationException(
                $"The replacement terminator defines a machine value the replaced terminator did not: {old}");
        }
        _definitionsBySite[replacement] = definitions;
        _usesBySite[replacement] = uses;
    }

    /// <summary>The values <paramref name="terminator"/> defines, with their new versions.</summary>
    internal IReadOnlyList<(IrSsaValue Value, int Version)> Definitions(IrTerminator terminator) =>
        _definitionsBySite.TryGetValue(terminator, out (IrSsaValue Value, int Version)[]? definitions)
            ? definitions
            : [];

    /// <summary>
    /// The version of <paramref name="value"/> before the statement at <paramref name="statementIndex"/>
    /// in <paramref name="block"/> runs: the nearest earlier definition in the block, else the block
    /// entry version (a phi result counts as the entry version). An index of
    /// <c>block.Statements.Count</c> means before the terminator. A statement a pass inserted after
    /// this SSA was built has no site here: it is skipped when it defines no machine value, and a
    /// defining statement without a site is a pass bug and throws.
    /// </summary>
    public int VersionBefore(IrBlock block, int statementIndex, IrSsaValue value) {
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
    /// The version of <paramref name="value"/> after the statement at <paramref name="statementIndex"/>
    /// in <paramref name="block"/> ran. An index of <c>block.Statements.Count</c> means after the
    /// terminator.
    /// </summary>
    public int VersionAfter(IrBlock block, int statementIndex, IrSsaValue value) {
        object site = statementIndex == block.Statements.Count ? block.Terminator : block.Statements[statementIndex];
        if (!_definitionsBySite.TryGetValue(site, out (IrSsaValue Value, int Version)[]? siteDefinitions)) {
            RequireNonDefining(site);
            siteDefinitions = [];
        }
        int defined = VersionOf(siteDefinitions, value);
        return defined != NoVersion ? defined : VersionBefore(block, statementIndex, value);
    }

    /// <summary>A site this SSA never recorded must not define a machine value: a pass rebuilt or
    /// inserted it without <see cref="Replace(IrStatement, IrStatement)"/>, so its writes would be
    /// invisible to version queries.</summary>
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
    public IReadOnlyList<IrSsaUse> Uses(int version) =>
        _usesByVersionId.TryGetValue(version, out List<IrSsaUse>? uses) ? uses : [];

    /// <summary>
    /// The uses of <paramref name="version"/> minus the dead ones: a phi is dead when its result
    /// reaches no statement or terminator, and the use a partial definition makes of the previous
    /// version is dead when the new version reaches no use.
    /// </summary>
    public IReadOnlyList<IrSsaUse> LiveUses(int version) =>
        _liveUsesByVersionId.TryGetValue(version, out List<IrSsaUse>? uses) ? uses : [];

    /// <summary>Whether <paramref name="version"/> reaches a statement or terminator.</summary>
    public bool IsLive(int version) => LiveUses(version).Count != 0;

    /// <summary>The phis at the top of <paramref name="block"/>.</summary>
    public IReadOnlyList<IrSsaPhi> PhisOf(IrBlock block) =>
        _phisByBlock.TryGetValue(block, out List<IrSsaPhi>? phis) ? phis : [];

    /// <summary>
    /// Whether every value covered by <paramref name="reads"/> and <paramref name="tokenReads"/> has the
    /// same version before position <paramref name="from"/> and before position <paramref name="to"/>:
    /// the legality rule for moving or merging an expression between the two places.
    /// </summary>
    public bool SameVersions(IrValues reads, IrTokens tokenReads, (IrBlock Block, int Index) from,
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
            versions[v] = NewVersion((IrSsaValue)v, IrSsaDefinitionKind.MethodEntry, block, -1, -1);
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
                plan.Result = NewVersion(plan.Value, IrSsaDefinitionKind.Phi, block, -1, -1);
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
        uint reads = IrSsaValues.ValueBits(effects.Reads) | IrSsaValues.TokenBits(effects.TokenReads);
        if (effects.MayFault) {
            reads |= IrSsaValues.All;
        }
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
                current[v] = NewVersion((IrSsaValue)v, IrSsaDefinitionKind.Definition, block, index, -1);
                definitions.Add(((IrSsaValue)v, current[v]));
            }
        }
        for (int v = 0; v < ValueCount; v++) {
            if ((partial & (1u << v)) == 0) {
                continue;
            }
            int previous = current[v];
            int fresh = NewVersion((IrSsaValue)v, IrSsaDefinitionKind.PartialDefinition, block, index, previous);
            current[v] = fresh;
            definitions.Add(((IrSsaValue)v, fresh));
            bool conditional = (reads & (1u << v)) == 0;
            AddUse(previous, new IrSsaUse(block, index, statement, terminator, null, null) {
                ConditionalNewVersion = conditional ? fresh : -1
            });
        }
        foreach ((IrSsaValue usedValue, int usedVersion) in uses) {
            if ((partial & (1u << (int)usedValue)) != 0) {
                continue;
            }
            AddUse(usedVersion, new IrSsaUse(block, index, statement, terminator, null, null));
        }
        _definitionsBySite[site] = [.. definitions];
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
                    AddUse(version, new IrSsaUse(block, -1, null, null, phi, predecessor));
                }
                if (plan.HasEntryOperand && entryVersions is not null) {
                    AddUse(entryVersions[(int)plan.Value],
                        new IrSsaUse(block, -1, null, null, phi, block));
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
            List<IrSsaUse>? live = null;
            for (int i = 0; i < uses.Count; i++) {
                if (IsLiveUse(uses[i])) {
                    live?.Add(uses[i]);
                } else if (live is null) {
                    live = new List<IrSsaUse>(uses.Count);
                    live.AddRange(uses.Take(i));
                }
            }
            _liveUsesByVersionId[id] = live ?? uses;
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

    private int NewVersion(IrSsaValue value, IrSsaDefinitionKind kind, IrBlock block, int statementIndex,
        int previous) {
        int version = _infos.Count;
        _infos.Add(new IrSsaVersionInfo(value, kind, block, statementIndex, previous));
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
