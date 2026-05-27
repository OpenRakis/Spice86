namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Intermediate;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using System.Linq;

/// <summary>
/// Mutable draft of a partition, finalized via <see cref="ToPartition"/>.
/// </summary>
internal sealed class CfgPartitionDraft {
    public CfgPartitionDraft(int id, CfgCodePartitionKind kind, CfgBlock entryBlock, string name) {
        Id = id;
        Kind = kind;
        EntryBlock = entryBlock;
        Name = name;
        Blocks.Add(entryBlock);
    }

    public int Id { get; }
    public CfgCodePartitionKind Kind { get; }
    public CfgBlock EntryBlock { get; }
    public string Name { get; }
    public HashSet<CfgBlock> Blocks { get; } = new();
    public List<CfgCodePartitionEntry> Entries { get; } = new();

    public CfgCodePartition ToPartition() => new() {
        Id = Id,
        Kind = Kind,
        Name = Name,
        Blocks = CfgPartitionOrdering.BlocksByAddressAndId(Blocks).ToArray(),
        Entries = Entries.OrderBy(entry => entry.Block.Id).ThenBy(entry => entry.Kind).ThenBy(entry => entry.Address).ToArray()
    };
}
