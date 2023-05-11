namespace Spice86.Core.Emulator.Function;

using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// 
/// </summary>
public interface IOverrideSupplier {
    public Dictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
        int programStartAddress,
        Machine machine);
}