namespace Spice86.Shared.Emulator.Input.Joystick;

/// <summary>
/// Abstraction over "where the joystick state comes from". The Core
/// <c>Gameport</c> device only depends on this interface; concrete
/// implementations live in the UI layer (SDL, keyboard fallback,
/// scripted timeline, MCP-driven) so that the emulator core stays
/// free of UI and SDL dependencies.
/// </summary>
/// <remarks>
/// Implementations are expected to be cheap to call — the
/// <c>Gameport</c> reads the latest sample on every port-<c>0x201</c>
/// access. Implementations may either poll their backing device on
/// each call or maintain a cached snapshot updated by an external
/// pump (e.g. an Avalonia <c>DispatcherTimer</c>).
/// </remarks>
public interface IGameportInputSource {

    /// <summary>
    /// Human-readable name of this input source (e.g.
    /// <c>"SDL: Xbox Controller (GUID: 030000005e040000…)"</c>).
    /// Used by logs and the mapper UI.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Returns the latest two-stick state. Must be safe to call from
    /// the emulator thread on every port read.
    /// </summary>
    /// <returns>The current <see cref="VirtualJoystickState"/>.</returns>
    VirtualJoystickState GetCurrentState();
}
