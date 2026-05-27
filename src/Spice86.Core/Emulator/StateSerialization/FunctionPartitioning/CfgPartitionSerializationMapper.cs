namespace Spice86.Core.Emulator.StateSerialization.FunctionPartitioning;

using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using System.Linq;

/// <summary>
/// Projects typed CFG partition overlay data to the stable JSON DTO model.
/// </summary>
internal sealed class CfgPartitionSerializationMapper {
    public CfgFunctionPartitioningResult Map(CfgPartitionedProgram program) => new() {
        Partitions = program.Partitions
            .OrderBy(partition => partition.Id)
            .Select(MapPartition)
            .ToArray(),
        Transfers = program.Transfers
            .Select(MapTransfer)
            .OrderBy(transfer => transfer.FromPartition)
            .ThenBy(transfer => transfer.ToPartition)
            .ThenBy(transfer => transfer.FromBlock)
            .ThenBy(transfer => transfer.ToBlock)
            .ThenBy(transfer => transfer.Kind)
            .ToArray()
    };

    private static CfgPartitionInfo MapPartition(CfgCodePartition partition) => new() {
        Id = partition.Id,
        Kind = ToJsonString(partition.Kind),
        Name = partition.Name,
        Blocks = partition.Blocks.Select(block => block.Id).ToArray(),
        Entries = partition.Entries.Select(MapEntry).ToArray()
    };

    private static CfgPartitionEntryInfo MapEntry(CfgCodePartitionEntry entry) => new() {
        Block = entry.Block.Id,
        Address = entry.Address.ToString(),
        Kind = ToJsonString(entry.Kind)
    };

    private static CfgPartitionTransferInfo MapTransfer(CfgCodePartitionTransfer transfer) => new() {
        Kind = ToJsonString(transfer.Kind),
        FromPartition = transfer.FromPartition.Id,
        ToPartition = transfer.ToPartition.Id,
        FromBlock = transfer.FromBlock.Id,
        ToBlock = transfer.ToBlock.Id,
        From = transfer.From.ToString(),
        Target = transfer.Target.ToString(),
        CallContinuationBlock = transfer.CallContinuationNode?.ContainingBlock?.Id,
        CallContinuationAddress = transfer.CallContinuationAddress?.ToString()
    };

    private static string ToJsonString(Enum kind) {
        string name = kind.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}