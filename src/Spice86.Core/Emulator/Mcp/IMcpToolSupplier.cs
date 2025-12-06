namespace Spice86.Core.Emulator.Mcp;

using System.Reflection;

internal interface IMcpToolSupplier {
    IEnumerable<Assembly> GetMcpToolAssemblies();

    IEnumerable<object> GetMcpServices();
}
