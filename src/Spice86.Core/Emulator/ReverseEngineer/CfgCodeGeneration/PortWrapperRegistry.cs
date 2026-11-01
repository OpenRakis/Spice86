namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Emulator.Memory;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

/// <summary>
/// Maps a constant (port, access width, direction) to the <c>ClassName.MethodName</c> call of the
/// typed port wrapper declared for it with <see cref="PortWrapperAttribute"/>. Built once by
/// reflection over the public types of the Spice86.Core assembly.
/// </summary>
internal static class PortWrapperRegistry {
    private static readonly Dictionary<(ushort Port, BitWidth Width, PortDirection Direction), string> Calls;

    static PortWrapperRegistry() {
        Calls = [];
        foreach (Type type in typeof(PortWrapperAttribute).Assembly.GetExportedTypes()) {
            foreach (MethodInfo method in type.GetMethods(
                         BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
                PortWrapperAttribute? attribute = method.GetCustomAttribute<PortWrapperAttribute>();
                if (attribute is null) {
                    continue;
                }
                (ushort Port, BitWidth Width, PortDirection Direction) key =
                    (attribute.Port, attribute.Width, attribute.Direction);
                string call = $"{type.Name}.{method.Name}";
                if (!Calls.TryAdd(key, call)) {
                    throw new InvalidOperationException(
                        $"Two port wrappers declare port 0x{attribute.Port:X4} {attribute.Width} {attribute.Direction}: {Calls[key]} and {call}.");
                }
            }
        }
    }

    /// <summary>
    /// Finds the wrapper call for a (port, width, direction) access.
    /// </summary>
    /// <param name="port">The constant I/O port.</param>
    /// <param name="width">The access width.</param>
    /// <param name="direction">The access direction.</param>
    /// <param name="call">The <c>ClassName.MethodName</c> text of the wrapper, when found.</param>
    /// <returns><see langword="true"/> when a wrapper is declared for the access.</returns>
    internal static bool TryFind(ushort port, BitWidth width, PortDirection direction,
        [NotNullWhen(true)] out string? call) =>
        Calls.TryGetValue((port, width, direction), out call);
}
