namespace Spice86.Core.Emulator.Devices.Sound;

public enum Mode {
    Opl2, DualOpl2, Opl3, Opl3Gold
}

    public struct Control {
        public Control() { }
        public byte Index { get; set; }
        public byte LVol { get; set; } = OPL.DefaultVolume;
        public byte RVol { get; set; }

        public bool IsActive { get; set; }
        public bool UseMixer { get; set; }
    }

public class OPLTimer {
    /// <summary>
    /// Rounded down start time
    /// </summary>
    private double _start = 0.0;

    /// <summary>
    /// Time when you overflow
    /// </summary>
    private double _trigger = 0.0;

    /// <summary>
    /// Clock Interval in Milliseconds
    /// </summary>
    private readonly double _clockInterval = 0.0;

    /// <summary>
    /// Cycle interval
    /// </summary>
    private double _counterInterval = 0.0;

    private byte _counter = 0;

    private bool _enabled = false;
    private bool _overflow = false;
    private bool _masked = false;
    public OPLTimer(short micros) {
        _clockInterval = micros * 0.001;
        SetCounter(0);
    }

    public void SetCounter(byte val) {
        _counter = val;
        // Interval for next cycle
        _counterInterval = (256 - _counter) * _clockInterval;
    }

    public void Reset() {
        // On a reset make sure the start is in sync with the next cycle
        _overflow = false;
    }

    public void SetMask(bool set) {
        _masked = set;
        if (_masked) {
            _overflow = false;
        }
    }

    public void Stop() {
        _enabled = false;
    }

    public void Start(double time) {
        // Only properly start when not running before
        if (!_enabled) {
            _enabled = true;
            _overflow = false;
            // Sync start to the last clock interval
            double clockMod = Math.IEEERemainder(time, _clockInterval);

            _start = time - clockMod;
            // Overflow trigger
            _trigger = _start + _counterInterval;
        }
    }

    public bool Update(double time) {
        if (_enabled && (time >= _trigger)) {
            // How far into the next cycle
            double deltaTime = time - _trigger;
            // Sync start to last cycle
            double counter_mod = Math.IEEERemainder(deltaTime, _counterInterval);

            _start = time - counter_mod;
            _trigger = _start + _counterInterval;
            // Only set the overflow flag when not masked
            if (!_masked)
                _overflow = true;
        }
        return _overflow;
    }
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
    public const byte DefaultVolume = 0xff;

    public MixerChannel Channel { get; private set; } = new();

    /// <summary>
    /// The cache for 2 chips or an OPL3
    /// </summary>
    public byte[] Cache { get; private set; } = new byte[512];

    public OPL(OplMode mode) {
    }
}