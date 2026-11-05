namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using System.Linq;

/// <summary>
/// The cross-method facts <see cref="IrSegmentSummaries"/> hands to <see cref="IrSegmentValues"/>:
/// for each entry block of a method, the registers holding a known value at every way in and the
/// registers sharing one entry root because every way in gives them the same value; and, for the
/// register versions a call defines, what the callees leave behind. <see cref="None"/> carries no
/// fact at all, which keeps every entry version its own symbolic root and every call-defined
/// version unknown.
/// </summary>
internal sealed class IrSegmentFacts {
    private readonly Dictionary<IrBlock, IrSegmentEntryFact> _entries = new();
    private readonly Dictionary<int, IrSegmentCallValue> _calls = new();

    /// <summary>No fact: the behaviour of the single-method analysis.</summary>
    public static IrSegmentFacts None { get; } = new();

    internal void SetEntry(IrBlock block, IrSegmentEntryFact fact) => _entries[block] = fact;

    internal void SetCall(int version, IrSegmentCallValue value) => _calls[version] = value;

    internal IrSegmentEntryFact? EntryFactOf(IrBlock block) => _entries.GetValueOrDefault(block);

    internal bool TryCallValue(int version, out IrSegmentCallValue? value) =>
        _calls.TryGetValue(version, out value);

    /// <summary>Whether two fact sets hold the same entry and call values.</summary>
    internal bool ContentEquals(IrSegmentFacts? other) =>
        other is not null
        && _entries.Count == other._entries.Count
        && _calls.Count == other._calls.Count
        && _entries.All(pair => other._entries.TryGetValue(pair.Key, out IrSegmentEntryFact? fact)
            && fact is not null && FactEquals(pair.Value, fact))
        && _calls.All(pair => other._calls.TryGetValue(pair.Key, out IrSegmentCallValue? value)
            && value is not null && pair.Value.Equals(value));

    private static bool FactEquals(IrSegmentEntryFact first, IrSegmentEntryFact second) =>
        first.KnownValues.Count == second.KnownValues.Count
        && first.EqualTo.Count == second.EqualTo.Count
        && first.KnownValues.All(pair => second.KnownValues.TryGetValue(pair.Key, out ushort value)
            && value == pair.Value)
        && first.EqualTo.All(pair => second.EqualTo.TryGetValue(pair.Key, out IrSsaValue value)
            && value == pair.Value);
}
