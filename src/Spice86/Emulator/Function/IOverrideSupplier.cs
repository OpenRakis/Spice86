namespace Spice86.Emulator.Function;

using Spice86.Emulator.VM;
using Spice86.Emulator.Memory;

using System.Collections.Generic;

public interface IOverrideSupplier {

    public Dictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
        int programStartAddress,
        Machine machine);
}