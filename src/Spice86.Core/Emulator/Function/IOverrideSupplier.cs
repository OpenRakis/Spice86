namespace Spice86.Core.Emulator.Function;

using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Provides an interface for generating function information overrides for machine code.
/// </summary>
public interface IOverrideSupplier {
    
    /// <summary>
    /// Generates function information overrides for the given target program.
    /// </summary>
    /// <param name="programStartAddress">The start address of the program.</param>
    /// <param name="machine">The emulator machine.</param>
    /// <returns>A dictionary containing the generated function information overrides.</returns>
    public Dictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
        int programStartAddress,
        Machine machine);
}
