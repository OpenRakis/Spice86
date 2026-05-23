namespace Spice86.Shared.Emulator.Input.Joystick.Mapping;

/// <summary>
/// Identifies a logical gameport button. Stick A has buttons 1 and
/// 2; stick B has buttons 1 and 2 (= overall buttons 3 and 4 from
/// the perspective of a four-button DOS game).
/// </summary>
public enum VirtualButton {
    /// <summary>Stick A, button 1.</summary>
    StickAButton1,

    /// <summary>Stick A, button 2.</summary>
    StickAButton2,

    /// <summary>Stick B, button 1.</summary>
    StickBButton1,

    /// <summary>Stick B, button 2.</summary>
    StickBButton2,

    /// <summary>The button is unmapped and should be ignored.</summary>
    None
}
