namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Intermediate;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

/// <summary>
/// Factory for creating synthetic <see cref="CfgPartitionDraft"/> instances.
/// </summary>
internal static class CfgPartitionDraftFactory {
    /// <summary>
    /// Creates a synthetic partition with the given blocks and entries.
    /// </summary>
    public static CfgPartitionDraft CreateSynthetic(int id, CfgBlock entryBlock, HashSet<CfgBlock> blocks, List<CfgCodePartitionEntry> entries) {
        CfgPartitionDraft partition = new(id, CfgCodePartitionKind.Synthetic, entryBlock,
            CfgPartitionNameProvider.CreateUnknownName(entryBlock.Entry.Address));
        partition.Blocks.UnionWith(blocks);
        partition.Entries.AddRange(entries);
        return partition;
    }

    /// <summary>
    /// Creates a shared-entry <see cref="CfgCodePartitionEntry"/> for the given block.
    /// </summary>
    public static CfgCodePartitionEntry CreateSharedEntry(CfgBlock block) => new() {
        Node = block.Entry,
        Kind = CfgCodePartitionEntryKind.SharedEntry
    };
}
