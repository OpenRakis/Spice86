namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
/// Represents a hardware component that can raise interrupts
/// </summary>
public interface IRequestInterrupt {
    /// <summary>
    /// Raises a hardware interrupt.
    /// </summary>
    void RaiseInterruptRequest();
}