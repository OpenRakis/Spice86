namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Intermediate;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using System.Linq;

/// <summary>
/// Root evidence for a recovered observed or entry partition.
/// </summary>
internal sealed class CfgPartitionRoot(CfgBlock entryBlock, CfgCodePartitionKind kind, string name)
{
    public CfgBlock EntryBlock { get; } = entryBlock;
    public CfgCodePartitionKind Kind { get; } = kind;
    public string Name { get; } = name;
    public List<CfgCodePartitionEntry> Entries { get; } = new();

    public void AddEntry(ICfgNode node, CfgCodePartitionEntryKind kind) {
        bool exists = Entries.Any(entry => entry.Node.Equals(node) && entry.Kind == kind);
        if (exists) {
            return;
        }
        Entries.Add(new CfgCodePartitionEntry {
            Node = node,
            Kind = kind
        });
    }
}
