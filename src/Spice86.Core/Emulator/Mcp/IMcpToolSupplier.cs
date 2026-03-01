namespace Spice86.Core.Emulator.Mcp;

using Spice86.Core.Emulator.Function;

using System.Reflection;

/// <summary>
/// Optional interface for override suppliers that want to register custom MCP tools.
/// Implement this alongside <see cref="IOverrideSupplier"/>.
/// </summary>
public interface IMcpToolSupplier {
    /// <summary>
    /// Returns assemblies containing [McpServerToolType] classes to register with the MCP server.
    /// </summary>
    IEnumerable<Assembly> GetMcpToolAssemblies();

    /// <summary>
    /// Returns singleton service instances to register in the MCP server's DI container.
    /// Each instance will be registered as its runtime type.
    /// Called after EmulatorMcpServices is already registered as singleton.
    /// </summary>
    IEnumerable<object> GetMcpServices();
}
