namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using System.Collections.Immutable;

/// <summary>The labels a lowered tree jumps to: block entries, and whether it re-enters the entry dispatcher.</summary>
internal sealed record GotoTargets(ImmutableHashSet<IGotoTarget> Labels, bool EntryDispatcher) {
    /// <summary>No jump at all.</summary>
    public static GotoTargets None { get; } = new(ImmutableHashSet<IGotoTarget>.Empty, false);

    /// <summary>The targets of both.</summary>
    public GotoTargets Union(GotoTargets other) => new(Labels.Union(other.Labels), EntryDispatcher || other.EntryDispatcher);
}
