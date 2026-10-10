namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
/// Read-only snapshot of one of the two GUS hardware timers.
/// </summary>
/// <param name="DelayMs">Countdown period in milliseconds.</param>
/// <param name="Value">Reload value written to the timer register.</param>
/// <param name="HasExpired">True once the timer has fired since the last reset.</param>
/// <param name="IsCountingDown">True while the timer is actively counting down.</param>
/// <param name="IsMasked">True when the timer is masked and will not set its expired flag.</param>
/// <param name="ShouldRaiseIrq">True when the timer raises an IRQ on expiry.</param>
public sealed record GusTimerState(
    double DelayMs,
    byte Value,
    bool HasExpired,
    bool IsCountingDown,
    bool IsMasked,
    bool ShouldRaiseIrq);
