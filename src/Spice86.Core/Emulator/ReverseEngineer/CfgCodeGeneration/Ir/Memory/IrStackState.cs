namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

using System.Linq;

/// <summary>
/// The forward dataflow value of <see cref="IrStackSlots"/>: the SP offset and, for each live
/// delta below the current base, the definitions that may have supplied it. Copies of a delta are
/// the union of several pushes only when they agree on the delta; the merge of paths at different
/// deltas keeps the deltas separate. <see cref="PartialSlots"/> holds the deltas defined on some
/// reaching paths only: their definition sets are unions that miss the paths where nothing wrote
/// the slot. Equality compares the slot map sorted by delta (a
/// <see cref="SortedDictionary{TKey,TValue}"/> has no value equality of its own).
/// </summary>
internal sealed class IrStackState : IEquatable<IrStackState> {
    private IrStackState(IrStackOffset offset, IrStackOffset bp,
        SortedDictionary<int, HashSet<IrStackSlotDefinition>> slots,
        HashSet<int> partialSlots) {
        Offset = offset;
        Bp = bp;
        Slots = slots;
        PartialSlots = partialSlots;
    }

    /// <summary>The bottom value: nothing reached.</summary>
    public static IrStackState Unreached { get; } =
        new(IrStackOffset.Unreached, IrStackOffset.Unreached,
            new SortedDictionary<int, HashSet<IrStackSlotDefinition>>(), []);

    /// <summary>SP at its base with no slot.</summary>
    public IrStackOffset Offset { get; }

    /// <summary>The stack offset BP holds: set by <c>mov bp, sp</c> to the SP offset of that
    /// point, unknown by default and after any statement that may write BP or SS or rebase the
    /// offset.</summary>
    public IrStackOffset Bp { get; }

    /// <summary>Delta below the current base to the definitions that may have written it.</summary>
    public SortedDictionary<int, HashSet<IrStackSlotDefinition>> Slots { get; }

    /// <summary>The deltas of <see cref="Slots"/> defined on some reaching paths only.</summary>
    public HashSet<int> PartialSlots { get; }

    /// <summary>The value at a method entry: SP equals the entry SP, no slots.</summary>
    public static IrStackState Entry => OfOffset(IrStackOffset.Entry);

    /// <summary>A state at <paramref name="offset"/> with no slot and no BP copy.</summary>
    public static IrStackState OfOffset(IrStackOffset offset) =>
        new(offset, IrStackOffset.Unknown, new SortedDictionary<int, HashSet<IrStackSlotDefinition>>(), []);

    /// <summary>SP starts a fresh base at <paramref name="statement"/>: the slots are gone.</summary>
    public static IrStackState Rebase(IrStatement statement) =>
        OfOffset(IrStackOffset.Known(statement, 0));

    /// <summary>The same slots and BP copy with <paramref name="offset"/>.</summary>
    public IrStackState WithOffset(IrStackOffset offset) => new(offset, Bp, Slots, PartialSlots);

    /// <summary>The same offset and slots with <paramref name="bp"/>.</summary>
    public IrStackState WithBp(IrStackOffset bp) => new(Offset, bp, Slots, PartialSlots);

    /// <summary>The same offset and BP copy with <paramref name="slots"/>.</summary>
    public IrStackState WithSlots(SortedDictionary<int, HashSet<IrStackSlotDefinition>> slots) =>
        new(Offset, Bp, slots, PartialSlots);

    /// <summary>A copy whose slot map can be mutated without touching this state.</summary>
    public IrStackState CopySlots() {
        SortedDictionary<int, HashSet<IrStackSlotDefinition>> slots = new(Slots.Comparer);
        foreach ((int delta, HashSet<IrStackSlotDefinition> definitions) in Slots) {
            slots[delta] = new HashSet<IrStackSlotDefinition>(definitions);
        }
        return new IrStackState(Offset, Bp, slots, new HashSet<int>(PartialSlots));
    }

    /// <summary>The same offset without the slots at or below <paramref name="delta"/>.</summary>
    public IrStackState WithoutSlotsBelow(int delta) {
        SortedDictionary<int, HashSet<IrStackSlotDefinition>> slots = new(Slots.Comparer);
        HashSet<int> partialSlots = [];
        foreach ((int slotDelta, HashSet<IrStackSlotDefinition> definitions) in Slots
                .Where(pair => pair.Key >= delta)) {
            slots[slotDelta] = definitions;
        }
        foreach (int slotDelta in PartialSlots.Where(slotDelta => slotDelta >= delta)) {
            partialSlots.Add(slotDelta);
        }
        return new IrStackState(Offset, Bp, slots, partialSlots);
    }

    /// <summary>The definitions that may have written the slot at <paramref name="delta"/>, empty
    /// when there is no such slot or the offset is not known.</summary>
    public IReadOnlySet<IrStackSlotDefinition> DefinitionsOf(int delta) {
        if (Offset.Kind != IrStackOffsetKind.Known
            || !Slots.TryGetValue(delta, out HashSet<IrStackSlotDefinition>? definitions)) {
            return new HashSet<IrStackSlotDefinition>();
        }
        return definitions;
    }

    /// <summary>The join of two states: unreached sides pass through, offsets join, and the slots
    /// of a known offset merge delta by delta. A delta reached on both sides is partial when it is
    /// defined on one side only or already partial on either.</summary>
    public static IrStackState Join(IrStackState first, IrStackState second) {
        IrStackOffset offset = IrStackOffset.Join(first.Offset, second.Offset);
        if (offset.Kind != IrStackOffsetKind.Known) {
            return OfOffset(offset);
        }
        if (first.Offset.Kind == IrStackOffsetKind.Unreached) {
            return second;
        }
        if (second.Offset.Kind == IrStackOffsetKind.Unreached) {
            return first;
        }
        SortedDictionary<int, HashSet<IrStackSlotDefinition>> slots = new();
        foreach ((int delta, HashSet<IrStackSlotDefinition> definitions) in first.Slots) {
            slots[delta] = new HashSet<IrStackSlotDefinition>(definitions);
        }
        foreach ((int delta, HashSet<IrStackSlotDefinition> definitions) in second.Slots) {
            if (!slots.TryGetValue(delta, out HashSet<IrStackSlotDefinition>? merged)) {
                merged = [];
                slots[delta] = merged;
            }
            merged.UnionWith(definitions);
        }
        HashSet<int> partialSlots = new(first.PartialSlots);
        partialSlots.UnionWith(second.PartialSlots);
        foreach (int delta in first.Slots.Keys.Where(delta => !second.Slots.ContainsKey(delta))) {
            partialSlots.Add(delta);
        }
        foreach (int delta in second.Slots.Keys.Where(delta => !first.Slots.ContainsKey(delta))) {
            partialSlots.Add(delta);
        }
        return new IrStackState(offset, IrStackOffset.Join(first.Bp, second.Bp), slots, partialSlots);
    }

    public bool Equals(IrStackState? other) {
        if (other is null || Offset != other.Offset || Bp != other.Bp
            || Slots.Count != other.Slots.Count
            || !PartialSlots.SetEquals(other.PartialSlots)) {
            return false;
        }
        using IEnumerator<KeyValuePair<int, HashSet<IrStackSlotDefinition>>> first = Slots.GetEnumerator();
        using IEnumerator<KeyValuePair<int, HashSet<IrStackSlotDefinition>>> second = other.Slots.GetEnumerator();
        while (first.MoveNext() && second.MoveNext()) {
            if (first.Current.Key != second.Current.Key || !first.Current.Value.SetEquals(second.Current.Value)) {
                return false;
            }
        }
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as IrStackState);

    public override int GetHashCode() => HashCode.Combine(Offset, Bp, Slots.Count);
}
