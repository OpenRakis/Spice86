namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Intermediate;

using System.Linq;

internal static class CfgPartitionOrdering {
    public static IOrderedEnumerable<CfgBlock> BlocksByAddressAndId(IEnumerable<CfgBlock> blocks) => blocks
        .OrderBy(block => block.Entry.Address)
        .ThenBy(block => block.Id);

    public static IOrderedEnumerable<T> ByBlockAddressAndId<T>(
        IEnumerable<T> values,
        Func<T, CfgBlock> blockSelector) => values
            .OrderBy(value => blockSelector(value).Entry.Address)
            .ThenBy(value => blockSelector(value).Id);

    public static IOrderedEnumerable<CfgPartitionRoot> RootsByEntryBlock(IEnumerable<CfgPartitionRoot> roots) => roots
        .OrderBy(root => root.EntryBlock.Entry.Address)
        .ThenBy(root => root.EntryBlock.Id);
}