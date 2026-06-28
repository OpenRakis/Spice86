using Spice86.Core.CLI;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Mcp;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using System.Reflection;

namespace BattleTechMcpTools;

public class BattleTechOverrideSupplier : IOverrideSupplier, IMcpToolSupplier
{
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
        ILoggerService loggerService, Configuration configuration,
        ushort programStartAddress, Machine machine)
    {
        return new Dictionary<SegmentedAddress, FunctionInformation>();
    }

    public IEnumerable<Assembly> GetMcpToolAssemblies()
    {
        return [typeof(BattleTechMcpTools).Assembly];
    }

    public IEnumerable<object> GetMcpServices()
    {
        return [];
    }
}
