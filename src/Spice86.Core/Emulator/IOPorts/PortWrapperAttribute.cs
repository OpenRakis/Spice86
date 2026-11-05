namespace Spice86.Core.Emulator.IOPorts;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Declares that a method is a typed wrapper for one I/O port: it forwards the access to
/// <see cref="IOPortDispatcher"/> at the declared <see cref="Port"/> and <see cref="Width"/>. The code
/// generator prints the wrapper call instead of the raw <c>InN</c> / <c>OutN</c> helper when a port
/// access uses this constant port, width and direction.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PortWrapperAttribute : Attribute {
    /// <summary>
    /// Initializes a new instance of the <see cref="PortWrapperAttribute"/> class.
    /// </summary>
    /// <param name="port">The I/O port the wrapper accesses.</param>
    /// <param name="width">The width of the access.</param>
    /// <param name="direction">The direction of the access.</param>
    public PortWrapperAttribute(ushort port, BitWidth width, PortDirection direction) {
        Port = port;
        Width = width;
        Direction = direction;
    }

    /// <summary>The I/O port the wrapper accesses.</summary>
    public ushort Port { get; }

    /// <summary>The width of the access.</summary>
    public BitWidth Width { get; }

    /// <summary>The direction of the access.</summary>
    public PortDirection Direction { get; }
}
