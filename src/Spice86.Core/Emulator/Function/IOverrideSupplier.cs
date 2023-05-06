namespace Spice86.Core.Emulator.Function;

using System.Collections.Generic;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared;
using Spice86.Shared.Emulator.Memory;

public interface IOverrideSupplier {
    public Dictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
        int programStartAddress,
        Machine machine);
}