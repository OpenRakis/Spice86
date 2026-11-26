namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

/// <summary>
/// The only writer of a block's statements and terminator. Every operation splices the block and
/// updates the SSA sites in the same call: a site the edit puts into the block gets use records
/// derived from its effects (<see cref="IrSsa.UsedBits"/>, the same rule the builder's renaming uses),
/// and its definitions follow the replace rules - the last replacement writing a value takes the old
/// version when the write kind and the previous version match, an earlier full write gets a fresh
/// version, and anything the editor cannot keep exact marks the SSA stale instead of repairing it.
/// Once the SSA is stale the editor still applies the list edits; the pipeline rebuilds the SSA
/// before the next pass reads it.
/// </summary>
internal sealed class IrEditor {
    private readonly GenerationStepTimer _timer;
    private IrSsa? _ssa;

    /// <summary>An editor over <paramref name="ssa"/> that times its rebuilds into
    /// <paramref name="timer"/>.</summary>
    internal IrEditor(IrSsa ssa, GenerationStepTimer timer) {
        _ssa = ssa;
        _timer = timer;
    }

    private IrEditor() {
        _timer = new GenerationStepTimer();
    }

    /// <summary>An editor without an SSA, for builders and passes that run before the first build.</summary>
    public static IrEditor WithoutSsa { get; } = new();

    /// <summary>The SSA this editor maintains.</summary>
    /// <exception cref="InvalidOperationException">On <see cref="WithoutSsa"/>.</exception>
    public IrSsa Ssa => _ssa
        ?? throw new InvalidOperationException("This editor has no SSA.");

    /// <summary>Discards the stale SSA and rebinds the editor on a fresh build of
    /// <paramref name="method"/>: a pass that must see its own earlier rewrite keeps working on
    /// correct records instead of reading stale ones. The rebuild is timed under
    /// <paramref name="step"/>.</summary>
    internal IrSsa Rebind(IrMethod method, string step) {
        _ssa = _timer.Measure(step, () => IrSsa.Build(method));
        return _ssa;
    }

    /// <summary>Appends <paramref name="statement"/> at the end of <paramref name="block"/>.</summary>
    public void Append(IrBlock block, IrStatement statement) =>
        Insert(block, block.Statements.Count, statement);

    /// <summary>Inserts <paramref name="statement"/> at <paramref name="index"/> of
    /// <paramref name="block"/>. An inserted statement that defines a machine value marks the SSA
    /// stale; a non-defining one only gets its derived uses.</summary>
    public void Insert(IrBlock block, int index, IrStatement statement) {
        if (_ssa is null || _ssa.IsStale) {
            block.Splice(index, 0, [statement]);
            return;
        }
        (uint full, uint partial) = IrSsaValues.WriteBits(statement.Effects);
        if ((full | partial) != 0) {
            _ssa.MarkStale($"a defining statement was inserted in block {block.Id}: {statement.Node.GetType().Name}");
            block.Splice(index, 0, [statement]);
            return;
        }
        block.Splice(index, 0, [statement]);
        RecordSite(block, index, statement, []);
    }

    /// <summary>Replaces the statement at <paramref name="index"/> of <paramref name="block"/> by
    /// <paramref name="replacement"/>.</summary>
    public void Replace(IrBlock block, int index, IrStatement replacement) =>
        Replace(block, index, [replacement]);

    /// <summary>Replaces the statement at <paramref name="index"/> of <paramref name="block"/> by
    /// <paramref name="replacements"/> (possibly none, which removes the statement).</summary>
    public void Replace(IrBlock block, int index, IReadOnlyList<IrStatement> replacements) {
        if (_ssa is null || _ssa.IsStale) {
            block.Splice(index, 1, replacements);
            return;
        }
        IrStatement old = block.Statements[index];
        ReplaceSite(block, index, old, replacements);
    }

    /// <summary>Removes the statement at <paramref name="index"/> of <paramref name="block"/>.</summary>
    public void Remove(IrBlock block, int index) {
        if (_ssa is null || _ssa.IsStale) {
            block.Splice(index, 1, []);
            return;
        }
        IrStatement old = block.Statements[index];
        (IrSsaValue Value, int Version)[] oldDefinitions = _ssa.RemoveSiteRecords(old);
        block.Splice(index, 1, []);
        DropOrphanedDefinitions(block, old, oldDefinitions, []);
    }

    /// <summary>Sets the terminator of <paramref name="block"/>. With the same edge targets in the
    /// same order it is a terminator replace under the SSA rules; different targets mark the SSA
    /// stale because the block structure changed.</summary>
    public void SetTerminator(IrBlock block, IrTerminator terminator) {
        if (_ssa is null || _ssa.IsStale || !block.HasTerminator) {
            block.SetTerminator(terminator);
            return;
        }
        IrTerminator old = block.Terminator;
        if (!SameTargets(old, terminator)) {
            _ssa.MarkStale($"the terminator of block {block.Id} changed the block's edges");
            block.SetTerminator(terminator);
            return;
        }
        ReplaceSite(block, block.Statements.Count, old, [terminator]);
    }

    /// <summary>Replaces the whole statement list and the terminator of <paramref name="block"/> and
    /// marks the SSA stale. For construction-time editors and the passes whose rewrite windows are too
    /// entangled to keep the SSA exact (<see cref="IrDoublePrecision"/>).</summary>
    public void ReplaceAll(IrBlock block, IReadOnlyList<IrStatement> statements, IrTerminator terminator) {
        _ssa?.MarkStale($"block {block.Id} was rewritten wholesale");
        block.ReplaceAll(statements);
        block.SetTerminator(terminator);
    }

    private static bool SameTargets(IrTerminator old, IrTerminator replacement) {
        if (old.Edges.Count != replacement.Edges.Count) {
            return false;
        }
        for (int i = 0; i < old.Edges.Count; i++) {
            if (!ReferenceEquals(old.Edges[i].Target, replacement.Edges[i].Target)) {
                return false;
            }
        }
        return true;
    }

    /// <summary>Splices <paramref name="replacements"/> over the site at <paramref name="index"/> and
    /// rebuilds the SSA sites: old definitions are dropped or reassigned per the replace rules, and
    /// every new site gets uses derived from its effects.</summary>
    private void ReplaceSite(IrBlock block, int index, object oldSite, IReadOnlyList<object> replacements) {
        IrSsa ssa = Ssa;
        (IrSsaValue Value, int Version)[] oldDefinitions = ssa.RemoveSiteRecords(oldSite);
        if (oldSite is IrStatement) {
            block.Splice(index, 1, (IReadOnlyList<IrStatement>)replacements);
        } else {
            block.SetTerminator((IrTerminator)replacements[^1]);
        }

        // Per value the old site defined: the last replacement writing it, or -1.
        Dictionary<IrSsaValue, int> lastWriter = [];
        foreach ((IrSsaValue value, _) in oldDefinitions) {
            lastWriter[value] = -1;
        }
        for (int i = 0; i < replacements.Count; i++) {
            (uint full, uint partial) = IrSsaValues.WriteBits(EffectsOf(replacements[i]));
            uint writes = full | partial;
            for (int v = 0; v < (int)IrSsaValue.Count; v++) {
                if ((writes & (1u << v)) == 0) {
                    continue;
                }
                if (!lastWriter.ContainsKey((IrSsaValue)v)) {
                    ssa.MarkStale($"a replacement of {SiteName(oldSite)} defines {(IrSsaValue)v} the replaced site did not");
                    return;
                }
                lastWriter[(IrSsaValue)v] = i;
            }
        }

        DropOrphanedDefinitions(block, oldSite, oldDefinitions, lastWriter);
        if (ssa.IsStale) {
            return;
        }

        for (int i = 0; i < replacements.Count; i++) {
            object site = replacements[i];
            IrEffects effects = EffectsOf(site);
            (uint full, uint partial) = IrSsaValues.WriteBits(effects);
            uint reads = IrSsa.ReadBits(effects);
            List<(IrSsaValue Value, int Version)> definitions = [];
            Dictionary<IrSsaValue, int> partialResult = [];
            for (int v = 0; v < (int)IrSsaValue.Count; v++) {
                IrSsaValue value = (IrSsaValue)v;
                if ((full & (1u << v)) == 0 && (partial & (1u << v)) == 0) {
                    continue;
                }
                (IrSsaValue _, int oldVersion) = OldDefinition(oldDefinitions, value);
                if (i == lastWriter[value]) {
                    IrSsaVersionInfo info = ssa.Info(oldVersion);
                    bool takesOld = (full & (1u << v)) != 0
                        ? info.Kind == IrSsaDefinitionKind.Definition
                        : info.Kind == IrSsaDefinitionKind.PartialDefinition
                            && info.Previous == ssa.VersionBefore(block, index + i, value);
                    if (!takesOld) {
                        ssa.MarkStale($"the replacement of {SiteName(oldSite)} does not take over the {value} version");
                        return;
                    }
                    definitions.Add((value, oldVersion));
                } else if ((full & (1u << v)) != 0) {
                    definitions.Add((value, ssa.NewDefinition(block, value, site)));
                } else {
                    ssa.MarkStale($"a replacement of {SiteName(oldSite)} partially writes {value} before the last writer");
                    return;
                }
                if ((partial & (1u << v)) != 0) {
                    partialResult[value] = definitions[^1].Version;
                }
            }
            List<(IrSsaValue Value, int Version, int ConditionalNewVersion)> uses = [];
            uint used = IrSsa.UsedBits(effects);
            for (int v = 0; v < (int)IrSsaValue.Count; v++) {
                if ((used & (1u << v)) == 0) {
                    continue;
                }
                IrSsaValue value = (IrSsaValue)v;
                int version = ssa.VersionBefore(block, index + i, value);
                bool conditional = (partial & (1u << v)) != 0 && (reads & (1u << v)) == 0;
                uses.Add((value, version, conditional ? partialResult[value] : -1));
            }
            ssa.RecordSite(block, site, uses, definitions);
        }
    }

    /// <summary>Drops the old definitions no replacement writes when they have no use left, else
    /// marks the SSA stale.</summary>
    private void DropOrphanedDefinitions(IrBlock block, object oldSite,
        (IrSsaValue Value, int Version)[] oldDefinitions,
        Dictionary<IrSsaValue, int> lastWriter) {
        IrSsa ssa = Ssa;
        foreach ((IrSsaValue value, int version) in oldDefinitions) {
            if (lastWriter.TryGetValue(value, out int writer) && writer >= 0) {
                continue;
            }
            if (ssa.HasAnyUse(version)) {
                ssa.MarkStale($"the removal of {SiteName(oldSite)} in block {block.Id} leaves a live {value} version without a definition");
                return;
            }
            ssa.DropDefinition(version);
        }
    }

    private static (IrSsaValue Value, int Version) OldDefinition(
        (IrSsaValue Value, int Version)[] definitions, IrSsaValue value) {
        foreach ((IrSsaValue defined, int version) in definitions) {
            if (defined == value) {
                return (defined, version);
            }
        }
        throw new InvalidOperationException($"The replaced site did not define {value}.");
    }

    private static IrEffects EffectsOf(object site) => site switch {
        IrStatement statement => statement.Effects,
        IrTerminator terminator => terminator.Effects,
        _ => throw new InvalidOperationException($"Not an SSA site: {site}")
    };

    private static string SiteName(object site) => site switch {
        IrStatement statement => statement.Node.GetType().Name,
        IrTerminator terminator => terminator.GetType().Name,
        _ => site.ToString() ?? "site"
    };

    /// <summary>Records a non-defining <paramref name="site"/> with uses derived from its effects.</summary>
    private void RecordSite(IrBlock block, int index, object site,
        List<(IrSsaValue Value, int Version)> definitions) {
        IrSsa ssa = Ssa;
        List<(IrSsaValue Value, int Version, int ConditionalNewVersion)> uses = [];
        uint used = IrSsa.UsedBits(EffectsOf(site));
        for (int v = 0; v < (int)IrSsaValue.Count; v++) {
            if ((used & (1u << v)) == 0) {
                continue;
            }
            uses.Add(((IrSsaValue)v, ssa.VersionBefore(block, index, (IrSsaValue)v), -1));
        }
        ssa.RecordSite(block, site, uses, definitions);
    }
}
