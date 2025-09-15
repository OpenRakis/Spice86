namespace Spice86.Shared.Interfaces;

using Spice86.Shared.Emulator.VM.Breakpoint.Serializable;

/// <summary>
/// This interface defines a method for generating a serializable object that encapsulates all breakpoints of the internal debugger
/// </summary>
public interface ISerializableBreakpointsSource {
    /// <summary>
    /// Creates a serializable representation of all breakpoints in the class
    /// </summary>
    /// <returns>A SerializedBreakpoints object containing all the internal debugger breakpoints.</returns>
    public SerializableUserBreakpointCollection CreateSerializableBreakpoints();
    }
