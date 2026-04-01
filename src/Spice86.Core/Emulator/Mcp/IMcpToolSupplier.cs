namespace Spice86.Core.Emulator.Mcp;

using System.Reflection;

public interface IMcpToolSupplier {
    IEnumerable<Assembly> GetMcpToolAssemblies();

    IEnumerable<object> GetMcpServices();
}
