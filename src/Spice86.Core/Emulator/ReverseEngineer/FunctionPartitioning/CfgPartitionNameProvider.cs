namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;

using Spice86.Core.Emulator.Function;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Provides deterministic labels for recovered CFG partitions.
/// </summary>
internal static class CfgPartitionNameProvider {
    public static string GetPartitionName(SegmentedAddress address, FunctionCatalogue? functionCatalogue) {
        if (functionCatalogue != null && functionCatalogue.FunctionInformations.TryGetValue(address, out FunctionInformation? information)) {
            return information.Name;
        }
        return CreateUnknownName(address);
    }

    /// <summary>
    /// Base name for partitions with no catalogued function. It is intentionally address-free: the single
    /// address suffix is owned by the C# generator (<c>GeneratorAnalysis</c>), which appends it once to form
    /// a unique method name. Baking the address triplet in here too would duplicate it in the generated name.
    /// </summary>
    public static string CreateUnknownName(SegmentedAddress address) => "unknown";
}
