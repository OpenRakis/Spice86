namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;

using Spice86.Core.Emulator.Function;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Provides deterministic labels for recovered CFG partitions.
/// </summary>
internal static class CfgPartitionNameProvider {
    public static string GetPartitionName(SegmentedAddress address, FunctionCatalogue? functionCatalogue) {
        if (functionCatalogue != null && functionCatalogue.FunctionInformations.TryGetValue(address, out FunctionInformation? information)) {
            return information.ToString();
        }
        return CreateUnknownName(address);
    }

    public static string CreateUnknownName(SegmentedAddress address) =>
        $"unknown_{address.Segment:X4}_{address.Offset:X4}_{address.Linear:X5}";
}
