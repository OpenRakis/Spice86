namespace Spice86.Core.Emulator.Devices.Sound;

    public enum Mode {
        Opl2, DualOpl2, Opl3, Opl3Gold
    }

public class OPLTimer {

    public OPLTimer(short micros) {
        _clockInterval = micros * 0.001;
    }

    /// <summary>
    /// Rounded down start time
    /// </summary>
    private readonly double _start = 0.0;

    /// <summary>
    /// Time when you overflow
    /// </summary>
    private readonly double _trigger = 0.0;

    /// <summary>
    /// Clock Interval in Milliseconds
    /// </summary>
    private readonly double _clockInterval = 0.0;

    
    /// <summary>
    /// Cycle interval
    /// </summary>
    private readonly double _counterInterval = 0.0;

    private readonly bool _enabled = false;
    private readonly bool _overflow = false;
    private readonly bool _masked = false;
}

public class Chip {
    public Chip() {
        Timer0 = new(80);
        Timer1 = new(320);
     }

    /// <summary>
    /// Last selected register
    /// </summary>
    public OPLTimer Timer0 { get; private set; }
    public OPLTimer Timer1 { get; private set; }

    /// <summary>
    /// Check for it being a write to the timer
    /// </summary>
    public bool Write(ushort addr, byte val) {
        return false;
    }

    /// <summary>
    /// Read the current timer state, will use current double
    /// </summary>
    public byte Read() {
        return 0;
    }
}

public class OPL {
    public MixerChannel Channel { get; private set; } = new();

    /// <summary>
    /// The cache for 2 chips or an OPL3
    /// </summary>
    public byte[] Cache { get; private set; } = new byte[512];

    public OPL(OplMode mode) {

    }
}