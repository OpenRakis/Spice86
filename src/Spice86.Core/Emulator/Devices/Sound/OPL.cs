namespace Spice86.Core.Emulator.Devices.Sound;

using System.Runtime.InteropServices;

using Spice86.Core.Emulator.Devices.Sound.Ym7128b;
using Spice86.Core.Emulator.VM;

public enum Mode {
    Opl2, DualOpl2, Opl3, Opl3Gold
}

public struct Control {
    public Control() { }
    public byte Index { get; set; }
    public byte LVol { get; set; } = Opl.DefaultVolumeValue;
    public byte RVol { get; set; }

    public bool IsActive { get; set; }
    public bool UseMixer { get; set; }
}

public class OplTimer {
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
    public OplTimer(short micros) {
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
            double counterMod = Math.IEEERemainder(deltaTime, _counterInterval);

            _start = time - counterMod;
            _trigger = _start + _counterInterval;
            // Only set the overflow flag when not masked
            if (!_masked) {
                _overflow = true;
            }
        }
        return _overflow;
    }
}

public class Chip {
    private Machine _machine;
    public Chip(Machine machine) {
        _machine = machine;
        Timer0 = new(80);
        Timer1 = new(320);
    }

    /// <summary>
    /// Last selected register
    /// </summary>
    public OplTimer Timer0 { get; private set; }
    public OplTimer Timer1 { get; private set; }

    /// <summary>
    /// Check for it being a write to the timer
    /// </summary>
    public bool Write(ushort reg, byte val) {
        // if(reg == 0x02 || reg == 0x03 || reg == 0x04)
        // LOG(LOG_MISC,LOG_ERROR)("write adlib timer %X %X",reg,val);
        switch (reg) {
            case 0x02:
                Timer0.Update(TimeSpan.FromTicks(_machine.Timer.NumberOfTicks).TotalMilliseconds);
                Timer0.SetCounter(val);
                return true;
            case 0x03:
                Timer1.Update(TimeSpan.FromTicks(_machine.Timer.NumberOfTicks).TotalMilliseconds);
                Timer1.SetCounter(val);
                return true;
            case 0x04:
                // Reset overflow in both timers
                if ((val & 0x80) > 0) {
                    Timer0.Reset();
                    Timer1.Reset();
                } else {
                    double time = TimeSpan.FromTicks(_machine.Timer.NumberOfTicks).TotalMilliseconds;
                    if ((val & 0x1) > 0) {
                        Timer0.Start(time);
                    } else {
                        Timer0.Stop();
                    }

                    if ((val & 0x2) > 0) {
                        Timer1.Start(time);
                    } else {
                        Timer1.Stop();
                    }
                    Timer0.SetMask((val & 0x40) > 0);
                    Timer1.SetMask((val & 0x20) > 0);
                }
                return true;
        }
        return false;
    }

    /// <summary>
    /// Read the current timer state, will use current double
    /// </summary>
    public byte Read() {
        TimeSpan time = TimeSpan.FromTicks(_machine.Timer.NumberOfTicks);
        byte ret = 0;

        // Overflow won't be set if a channel is masked
        if (Timer0.Update(time.TotalMilliseconds)) {
            ret |= 0x40;
            ret |= 0x80;
        }
        if (Timer1.Update(time.TotalMilliseconds)) {
            ret |= 0x20;
            ret |= 0x80;
        }
        return ret;
    }
}

public class Opl {
    public const byte DefaultVolumeValue = 0xff;

    //public MixerChannel Channel { get; private set; } = new();

    /// <summary>
    /// The cache for 2 chips or an OPL3
    /// </summary>
    public byte[] Cache { get; private set; } = new byte[512];

    private Queue<Memory<float>> _fifo = new();

    private Mode _mode;

    private Chip[] _chip = new Chip[2];

    private Opl3Chip _oplChip = new();

    private byte _mem;

    private AdlibGold _adlibGold;

    // Playback related
    private double _lastRenderedMs = 0.0;
    private double _msPerFrame = 0.0;

    // Last selected address in the chip for the different modes

    private const int DefaultVolume = 0xff;


    [StructLayout(LayoutKind.Explicit)]
    private struct Reg {
        [FieldOffset(0)]
        public byte normal; 
        [FieldOffset(0)]
        public byte[] dual;

        public Reg() {
            dual = new byte[2];
        }
    }

    private Reg _reg = new();

    private Control _ctrl = new();

    public Opl(AdlibGold adlibGold, OplMode mode) {
        _adlibGold = adlibGold;
    }

    private void AdlibGoldControlWrite(byte val) {
        switch (_ctrl.Index) {
            case 0x04:
                _adlibGold.StereoControlWrite((byte)StereoProcessorControlReg.VolumeLeft,
                                               val);
                break;
            case 0x05:
                _adlibGold.StereoControlWrite((byte)StereoProcessorControlReg.VolumeRight,
                                               val);
                break;
            case 0x06:
                _adlibGold.StereoControlWrite((byte)StereoProcessorControlReg.Bass, val);
                break;
            case 0x07:
                _adlibGold.StereoControlWrite((byte)StereoProcessorControlReg.Treble, val);
                break;

            case 0x08:
                _adlibGold.StereoControlWrite((byte)StereoProcessorControlReg.SwitchFunctions,
                                               val);
                break;

            case 0x09: // Left FM Volume
                _ctrl.LVol = val;
                goto setvol;
            case 0x0a: // Right FM Volume
                _ctrl.RVol = val;
            setvol:
                if (_ctrl.UseMixer) {
                    // Dune CD version uses 32 volume steps in an apparent
                    // mistake, should be 128
                    _ctrl.LVol &= (byte) (0x1f / 31.0f);
                    _ctrl.RVol &= (byte) (0x1f / 31.0f);
                }
                break;

            case 0x18: // Surround
                _adlibGold.SurroundControlWrite(val);
                break;
        }
    }

    private byte AdlibGoldControlRead() {
        switch (_ctrl.Index) {
            case 0x00: // Board Options
                return 0x50; // 16-bit ISA, surround module, no
                             // telephone/CDROM
                             // return 0x70; // 16-bit ISA, no
                             // telephone/surround/CD-ROM

            case 0x09: // Left FM Volume
                return _ctrl.LVol;
            case 0x0a: // Right FM Volume
                return _ctrl.RVol;
            case 0x15: // Audio Relocation
                return 0x388 >> 3; // Cryo installer detection
        }
        return 0xff;
    }
}