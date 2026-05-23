namespace Spice86.Shared.Emulator.Input.Joystick;

/// <summary>
/// Read-only view of the byte that the emulated game would currently
/// see at I/O port <c>0x201</c>. Exposed so that the mapper UI and
/// MCP joystick tools can present live feedback without poking at
/// internal Core state.
/// </summary>
public interface IGameportPortReader {

    /// <summary>
    /// Returns the byte that a DOS application reading
    /// <see cref="GameportConstants.Port201"/> at this instant would
    /// receive. Calling this method must not have side effects
    /// (i.e. it must not arm the axis decay timers).
    /// </summary>
    /// <returns>The current value of port <c>0x201</c>.</returns>
    byte PeekPort201();
}
