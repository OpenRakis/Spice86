namespace Spice86.Emulator.Function;

using Spice86.Emulator.Machine;
using Spice86.Emulator.Memory;

using System.Collections.Generic;

public interface IOverrideSupplier {

    public Dictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
        int programStartAddress,
        Machine machine);
}