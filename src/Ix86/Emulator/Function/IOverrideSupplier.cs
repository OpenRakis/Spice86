using Ix86.Emulator.Memory;

using System.Collections.Generic;

namespace Ix86.Emulator.Function;
using Ix86.Emulator.Machine;

public interface IOverrideSupplier
{
    public Dictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
        int programStartAddress,
        Machine machine);
}
