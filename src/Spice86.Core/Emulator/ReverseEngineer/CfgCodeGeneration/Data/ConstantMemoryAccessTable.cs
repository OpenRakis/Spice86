namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Data;

/// <summary>
/// The constant-offset memory accesses of a program or a method: a sorted map
/// <see cref="ConstantMemoryAccess"/> to the <see cref="MemoryAccessDirection"/> it is accessed in,
/// sorted by segment register, offset, width.
/// </summary>
internal sealed class ConstantMemoryAccessTable {
    private readonly SortedDictionary<ConstantMemoryAccess, MemoryAccessDirection> _entries = new();

    /// <summary>The accesses, sorted by segment register, offset, width.</summary>
    public IEnumerable<KeyValuePair<ConstantMemoryAccess, MemoryAccessDirection>> Entries => _entries;

    /// <summary>Records an access in <paramref name="direction"/>; directions accumulate.</summary>
    public void Add(ConstantMemoryAccess access, MemoryAccessDirection direction) {
        _entries.TryGetValue(access, out MemoryAccessDirection existing);
        _entries[access] = existing | direction;
    }

    /// <summary>Folds every entry of <paramref name="other"/> into this table.</summary>
    public void Merge(ConstantMemoryAccessTable other) {
        foreach (KeyValuePair<ConstantMemoryAccess, MemoryAccessDirection> entry in other._entries) {
            Add(entry.Key, entry.Value);
        }
    }
}
