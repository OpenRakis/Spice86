namespace Spice86.Core.Emulator.IOPorts;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>
/// Declares one step of a fixed multi-access I/O port sequence a device method performs: a byte
/// write bound to one of the method's parameters, or the final byte read whose result the method
/// returns. Repeat it on the method, one attribute per access in <see cref="Order"/> order. The
/// code generator uses the declared sequence to recognize the port accesses and print the device
/// operation call instead; <see cref="PortWrapperAttribute"/> covers a single access and stays
/// unchanged. All declared accesses are byte accesses.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class PortSequenceStepAttribute : Attribute {
    /// <summary>
    /// Initializes a write step: the byte written to <paramref name="port"/> is bound to the method
    /// parameter <paramref name="parameterName"/>.
    /// </summary>
    /// <param name="order">The step's position in the sequence.</param>
    /// <param name="port">The I/O port the step writes.</param>
    /// <param name="direction">The access direction; writes must be <see cref="PortDirection.Out"/>.</param>
    /// <param name="parameterName">The name of the method parameter the written byte binds.</param>
    /// <exception cref="ArgumentException"><paramref name="direction"/> is not
    /// <see cref="PortDirection.Out"/>.</exception>
    public PortSequenceStepAttribute(int order, ushort port, PortDirection direction,
        string parameterName) {
        if (direction != PortDirection.Out) {
            throw new ArgumentException(
                "A bound step is a write and must be PortDirection.Out.", nameof(direction));
        }
        Order = order;
        Port = port;
        Direction = direction;
        ParameterName = parameterName;
    }

    /// <summary>
    /// Initializes a read step: the byte read from <paramref name="port"/> is the method's return
    /// value. A sequence has at most one read step, at the end.
    /// </summary>
    /// <param name="order">The step's position in the sequence.</param>
    /// <param name="port">The I/O port the step reads.</param>
    /// <param name="direction">The access direction; reads must be <see cref="PortDirection.In"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="direction"/> is not
    /// <see cref="PortDirection.In"/>.</exception>
    public PortSequenceStepAttribute(int order, ushort port, PortDirection direction) {
        if (direction != PortDirection.In) {
            throw new ArgumentException(
                "An unbound step is a read and must be PortDirection.In.", nameof(direction));
        }
        Order = order;
        Port = port;
        Direction = direction;
    }

    /// <summary>The step's position in the sequence; orders are unique, contiguous and start at
    /// zero.</summary>
    public int Order { get; }

    /// <summary>The I/O port the step accesses.</summary>
    public ushort Port { get; }

    /// <summary>The direction of the step's access.</summary>
    public PortDirection Direction { get; }

    /// <summary>The method parameter the written byte binds, or <see langword="null"/> when the
    /// step is the final read whose result the method returns.</summary>
    public string? ParameterName { get; }
}
