namespace Spice86.Core.Emulator.Mcp.Response;

/// <summary>
/// MCP response containing the state of both gameport joysticks and the raw port value.
/// </summary>
internal sealed record JoystickStateResponse {
    /// <summary>
    /// Whether joystick A is connected to the emulated gameport.
    /// </summary>
    public required bool JoystickAConnected { get; init; }

    /// <summary>
    /// Whether joystick B is connected to the emulated gameport.
    /// </summary>
    public required bool JoystickBConnected { get; init; }

    /// <summary>
    /// Joystick A X axis position, from 0.0 (left) to 1.0 (right).
    /// </summary>
    public required double AxisAX { get; init; }

    /// <summary>
    /// Joystick A Y axis position, from 0.0 (up) to 1.0 (down).
    /// </summary>
    public required double AxisAY { get; init; }

    /// <summary>
    /// Joystick B X axis position, from 0.0 (left) to 1.0 (right).
    /// </summary>
    public required double AxisBX { get; init; }

    /// <summary>
    /// Joystick B Y axis position, from 0.0 (up) to 1.0 (down).
    /// </summary>
    public required double AxisBY { get; init; }

    /// <summary>
    /// Whether joystick A button 1 is pressed.
    /// </summary>
    public required bool ButtonA1Pressed { get; init; }

    /// <summary>
    /// Whether joystick A button 2 is pressed.
    /// </summary>
    public required bool ButtonA2Pressed { get; init; }

    /// <summary>
    /// Whether joystick B button 1 is pressed.
    /// </summary>
    public required bool ButtonB1Pressed { get; init; }

    /// <summary>
    /// Whether joystick B button 2 is pressed.
    /// </summary>
    public required bool ButtonB2Pressed { get; init; }

    /// <summary>
    /// The raw byte value from reading I/O port 0x201.
    /// </summary>
    public required string PortValue { get; init; }
}
