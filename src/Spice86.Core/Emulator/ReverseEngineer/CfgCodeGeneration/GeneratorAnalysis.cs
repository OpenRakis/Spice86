namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// The first pass of the generator: walks the partitioned program, assigns names and labels to every node,
/// maps edges to their partition transfers, and produces the frozen <see cref="CfgGeneratorContext"/> that
/// the rest of the pipeline reads from.
/// </summary>
internal sealed class GeneratorAnalysis {
    private GeneratorAnalysis(CfgPartitionedProgram program, CfgGeneratorContext context) {
        Program = program;
        Context = context;
    }

    public CfgPartitionedProgram Program { get; }
    public CfgGeneratorContext Context { get; }

    public static GeneratorAnalysis Build(CfgPartitionedProgram program) {
        // Reverse index: every instruction back to its owning partition, so emitters can answer
        // "same method or cross-partition?" for any edge endpoint in O(1).
        Dictionary<ICfgNode, CfgCodePartition> partitionByNode = new();

        foreach (CfgCodePartition partition in program.Partitions) {
            foreach (CfgBlock block in partition.Blocks) {
                foreach (ICfgNode node in block.Instructions) {
                    partitionByNode[node] = partition;
                }
            }
        }

        // Address-free C# identifier per partition (the method name minus its address suffix). Kept
        // separate from the full method name because secondary-entry registrations append their own tags.
        Dictionary<CfgCodePartition, string> partitionBaseNames = program.Partitions.ToDictionary(
            partition => partition,
            partition => SanitizeIdentifier(partition.Name));

        // Method name = base name + the lowest entry address, so the symbol carries exactly one address.
        Dictionary<CfgCodePartition, string> methodNames = BuildMethodNames(program.Partitions, partitionBaseNames);

        // One goto label per node; Id disambiguates self-modifying variants that share an address.
        Dictionary<ICfgNode, string> labels = partitionByNode.Keys.ToDictionary(node => node, node => $"label_{AddressSuffix(node.Address)}_{node.Id}");
        // Stable cs1, cs2, ... field names assigned in segment order so output is deterministic across runs.
        Dictionary<ushort, string> segmentVariables = partitionByNode.Keys
            .Select(node => node.Address.Segment)
            .Distinct()
            .OrderBy(segment => segment)
            .Select((segment, index) => new { segment, name = $"cs{index + 1}" })
            .ToDictionary(entry => entry.segment, entry => entry.name);

        // Re-key transfers from (from,to) onto the typed CFG edges they correspond to. One transfer can
        // fan out to several typed successor edges; an empty fan-out means the recorded transfer has no
        // matching CFG edge, which is a hard inconsistency rather than a tolerated gap.
        List<(ResolvedCfgEdge Edge, CfgCodePartitionTransfer Transfer)> typedTransferEdges = [];
        foreach (CfgCodePartitionTransfer transfer in program.Transfers) {
            List<(ResolvedCfgEdge Edge, CfgCodePartitionTransfer Transfer)> transferEdges = GetTypedTransferEdges(transfer).ToList();
            if (transferEdges.Count == 0) {
                throw new NotSupportedException($"Partition transfer {transfer.Kind} from {transfer.From} to {transfer.Target} does not match any typed CFG successor edge.");
            }
            typedTransferEdges.AddRange(transferEdges);
        }
        Dictionary<ResolvedCfgEdge, CfgCodePartitionTransfer> transfersByEdge = typedTransferEdges
            .ToDictionary(entry => entry.Edge, entry => entry.Transfer);

        // Entries deduplicated by node and ordered by address; index 0 is the primary entry (loadOffset 0).
        Dictionary<CfgCodePartition, IReadOnlyList<CfgCodePartitionEntry>> entriesByPartition = program.Partitions.ToDictionary(
            partition => partition,
            partition => (IReadOnlyList<CfgCodePartitionEntry>)partition.Entries
                .GroupBy(entry => entry.Node)
                .Select(group => group.First())
                .OrderBy(entry => entry.Address.Linear)
                .ToList());

        Dictionary<SegmentedAddress, ICfgNode> blockEntryByAddress = BuildBlockEntryIndex(program);

        CfgGeneratorContext context = new(program, partitionByNode, methodNames, partitionBaseNames, labels,
            segmentVariables, transfersByEdge, entriesByPartition, blockEntryByAddress);
        return new GeneratorAnalysis(program, context);
    }

    /// <summary>
    /// Builds one C# method name per partition: <c>{baseName}_{SEG}_{OFF}_{LIN}</c> from the partition's
    /// lowest entry address, so the dumped symbol carries exactly one address triplet.
    /// <para>
    /// Self-modifying code can split several partitions onto the same entry address (a selector node plus its
    /// instruction variants all live at one address but are distinct CFG nodes in distinct partitions). Those
    /// partitions would otherwise collapse to one identical method name and produce duplicate C# methods
    /// (CS0111). When two or more partitions collide on a name, the primary entry node id is inserted ahead of
    /// the address triplet (<c>{baseName}_{nodeId}_{SEG}_{OFF}_{LIN}</c>) to disambiguate them, mirroring how
    /// labels already append the node id. The id stays before the trailing three tokens, so the address still
    /// round-trips through the Ghidra symbol parser. Names with no collision are left unchanged.
    /// </para>
    /// </summary>
    private static Dictionary<CfgCodePartition, string> BuildMethodNames(
        IReadOnlyList<CfgCodePartition> partitions,
        Dictionary<CfgCodePartition, string> partitionBaseNames) {
        Dictionary<CfgCodePartition, string> baseMethodNames = partitions.ToDictionary(
            partition => partition,
            partition => $"{partitionBaseNames[partition]}_{AddressSuffix(PrimaryEntryAddress(partition))}");

        HashSet<string> collidingNames = baseMethodNames.Values
            .GroupBy(name => name)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        return partitions.ToDictionary(
            partition => partition,
            partition => {
                string baseName = baseMethodNames[partition];
                if (!collidingNames.Contains(baseName)) {
                    return baseName;
                }
                return $"{partitionBaseNames[partition]}_{PrimaryEntryNodeId(partition)}_{AddressSuffix(PrimaryEntryAddress(partition))}";
            });
    }

    private static CfgCodePartitionEntry PrimaryEntry(CfgCodePartition partition) =>
        partition.Entries.OrderBy(entry => entry.Address.Linear).ThenBy(entry => entry.Node.Id).First();

    private static SegmentedAddress PrimaryEntryAddress(CfgCodePartition partition) => PrimaryEntry(partition).Address;

    private static int PrimaryEntryNodeId(CfgCodePartition partition) => PrimaryEntry(partition).Node.Id;

    /// <summary>
    /// Indexes block-entry nodes by their segmented address so the emitter can resolve a statically-known
    /// constant jump/fallthrough target that was never observed as a runtime edge but whose target block was
    /// nonetheless discovered. Only block entries are indexed because they are the only nodes that carry an
    /// emitted label a <c>goto</c> can target. Addresses shared by more than one block entry (self-modifying
    /// selector variants) are left out: they are ambiguous and must stay untested rather than pick a variant.
    /// </summary>
    private static Dictionary<SegmentedAddress, ICfgNode> BuildBlockEntryIndex(CfgPartitionedProgram program) {
        Dictionary<SegmentedAddress, ICfgNode> blockEntryByAddress = new();
        HashSet<SegmentedAddress> ambiguousAddresses = [];
        foreach (CfgCodePartition partition in program.Partitions) {
            foreach (CfgBlock block in partition.Blocks) {
                ICfgNode entry = block.Entry;
                if (blockEntryByAddress.TryGetValue(entry.Address, out ICfgNode? existing) && !ReferenceEquals(existing, entry)) {
                    ambiguousAddresses.Add(entry.Address);
                    continue;
                }
                blockEntryByAddress[entry.Address] = entry;
            }
        }
        foreach (SegmentedAddress ambiguous in ambiguousAddresses) {
            blockEntryByAddress.Remove(ambiguous);
        }
        return blockEntryByAddress;
    }

    private static IEnumerable<(ResolvedCfgEdge Edge, CfgCodePartitionTransfer Transfer)> GetTypedTransferEdges(CfgCodePartitionTransfer transfer) {
        // A transfer records only from/to nodes. To attach it to typed CFG edges we look up which successor
        // types of the source instruction actually reach the target: a single (from,to) pair can appear under
        // several successor types (e.g. both a normal and a call-return edge), yielding one tuple per type.
        if (transfer.FromNode is not CfgInstruction instruction) {
            return [(new ResolvedCfgEdge(transfer.FromNode, transfer.TargetNode, InstructionSuccessorType.Normal, transfer.Kind), transfer)];
        }

        return instruction.SuccessorsPerType
            .Where(entry => entry.Value.Contains(transfer.TargetNode))
            .Select(entry => (new ResolvedCfgEdge(transfer.FromNode, transfer.TargetNode, entry.Key, transfer.Kind), transfer));
    }

    private static string AddressSuffix(SegmentedAddress address) =>
        $"{address.Segment:X4}_{address.Offset:X4}_{address.Linear:X5}";

    private static string SanitizeIdentifier(string value) {
        char[] chars = value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        string result = new(chars);
        if (result.Length == 0 || char.IsDigit(result[0])) {
            return "generated_" + result;
        }
        return result;
    }
}
