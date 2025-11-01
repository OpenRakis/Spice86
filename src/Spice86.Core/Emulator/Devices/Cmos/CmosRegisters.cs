namespace Spice86.Core.Emulator.Devices.Cmos;

/// <summary>
/// Represents the CMOS RAM and related runtime state of the MC146818 RTC chip
/// </summary>
public class CmosRegisters {
    /// <summary>
    /// Total number of CMOS registers.
    /// </summary>
    public const int RegisterCount = 64;

    private readonly byte[] _registers = new byte[RegisterCount];

    /// <summary>
    /// Indexer for direct register access with bounds checking.
    /// </summary>
    /// <param name="index">Register index.</param>
    public byte this[int index] {
        get {
            if ((uint)index >= RegisterCount) {
                throw new ArgumentOutOfRangeException(nameof(index), index, "CMOS register index must be between 0 and 63.");
            }
            return _registers[index];
        }
        set {
            if ((uint)index >= RegisterCount) {
                throw new ArgumentOutOfRangeException(nameof(index), index, "CMOS register index must be between 0 and 63.");
            }
            _registers[index] = value;
        }
    }

    /// <summary>
    /// Gets or sets whether Non-Maskable Interrupts (NMI) are enabled.
    /// </summary>
    public bool NmiEnabled { get; set; }

    /// <summary>
    /// Gets or sets whether BCD encoding mode is active.
    /// </summary>
    public bool IsBcdMode { get; set; }

    /// <summary>
    /// Currently selected register index.
    /// </summary>
    public byte CurrentRegister { get; set; }

    /// <summary>
    /// Periodic/Rate/Divider timer state.
    /// </summary>
    public TimerState Timer { get; } = new();

    /// <summary>
    /// Timing of last internal events.
    /// </summary>
    public LastEventState Last { get; } = new();

    /// <summary>
    /// Gets or sets whether an update cycle has completed.
    /// </summary>
    public bool UpdateEnded { get; set; }

    /// <summary>
    /// Timer-related state (periodic IRQ behavior).
    /// </summary>
    public class TimerState {
        /// <summary>
        /// Gets or sets whether the periodic timer is currently enabled.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Gets or sets the Divider value.
        /// </summary>
        public byte Divider { get; set; }

        /// <summary>
        /// Gets or sets the delay between periodic events.
        /// </summary>
        public double Delay { get; set; }

        /// <summary>
        /// Gets or sets whether the last periodic interrupt was acknowledged.
        /// </summary>
        public bool Acknowledged { get; set; }
    }

    /// <summary>
    /// Tracks host-time timestamps of important RTC events.
    /// </summary>
    public class LastEventState {
        /// <summary>
        /// Time of last periodic timer event.
        /// </summary>
        public double Timer { get; set; }

        /// <summary>
        /// Time when the last update cycle ended.
        /// </summary>
        public double Ended { get; set; }

        /// <summary>
        /// Time of last alarm event.
        /// </summary>
        public double Alarm { get; set; }
    }
}
