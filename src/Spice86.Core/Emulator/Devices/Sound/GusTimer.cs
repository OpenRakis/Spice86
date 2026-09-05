namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
/// State for one of the two GUS hardware timers.
/// </summary>
/// <remarks>
/// 2022-2025 The DOSBox Staging Team
/// </remarks>
internal sealed class GusTimer {
    /// <summary>Countdown period in milliseconds.</summary>
    public double Delay { get; set; }

    /// <summary>Reload value written to the timer register.</summary>
    public byte Value { get; set; } = 0xFF;

    /// <summary>True once the timer has fired since the last reset.</summary>
    public bool HasExpired { get; set; } = true;

    /// <summary>True while the timer is actively counting down.</summary>
    public bool IsCountingDown { get; set; }

    /// <summary>True when the timer is masked and will not fire IRQs.</summary>
    public bool IsMasked { get; set; }

    /// <summary>True when the timer should raise an IRQ on expiry.</summary>
    public bool ShouldRaiseIrq { get; set; }

    /// <summary>Initialises a timer with the given period in milliseconds.</summary>
    public GusTimer(double delayMs) {
        Delay = delayMs;
    }
}
