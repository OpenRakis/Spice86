namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

public struct Control {
    public Control() { }
    public byte Index { get; set; }
    public bool IsActive { get; set; }
    public byte LVol { get; set; } = Opl.DefaultVolumeValue;
    public byte RVol { get; set; } = Opl.DefaultVolumeValue;
    public bool UseMixer { get; set; }
}

/// <summary>
/// The OPL3 / OPL2 / Adlib Gold OPL chip emulation class.
/// </summary>
public partial class Opl : DefaultIOPortHandler, IDisposable {
    public const byte DefaultVolumeValue = 0xff;
    private readonly AdlibGold _adlibGold;
    private readonly Queue<float[]> _audioQueue = new();
    private readonly DeviceThread _deviceThread;
    private readonly float[] _playBuffer = new float[2048];
    private readonly SoundChannel _soundChannel;
    private readonly System.Diagnostics.Stopwatch _wallClock = new();
    // Buffer for audio rendering
    private byte[] _cache = new byte[256];

    private Control _ctrl = new();
    private bool _disposed;
    private bool _dualOpl = false;

    // Playback related
    private double _lastRenderedMs = 0.0;

    private double _msPerFrame = 0.0;

    // Register cache
    private bool _newm; // OPL3 mode bit

    private Opl3Chip _oplChip;

    private OplMode _oplMode;

    private Reg _reg = new();

    /// <summary>
    /// Initializes a new instance of the OPL FM synth chip.
    /// </summary>
    /// <param name="fmSynthSoundChannel">The software mixer's sound channel for the OPL FM Synth chip.</param>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="ioPortDispatcher">The class that is responsible for dispatching
    /// ports reads and writes to classes that respond to them.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O port wasn't handled.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="pauseHandler">Class for handling pausing the emulator.</param>
    public Opl(SoundChannel fmSynthSoundChannel, OplMode oplMode, State state,
        IOPortDispatcher ioPortDispatcher, bool failOnUnhandledPort,
        ILoggerService loggerService, IPauseHandler pauseHandler)
        : base(state, failOnUnhandledPort, loggerService) {
        Timer0 = new OplTimer(80);
        Timer1 = new OplTimer(320);
        _adlibGold = new(loggerService);
        _soundChannel = fmSynthSoundChannel;
        _deviceThread = new DeviceThread(nameof(Opl), PlaybackLoopBody, pauseHandler, loggerService);
        _oplMode = oplMode;
        _dualOpl = oplMode != OplMode.Opl2;
        _msPerFrame = 1000.0 / 49716.0; // Typical OPL sample rate

        // Initialize OPL chip
        _oplChip = new Opl3Chip();

        InitPortHandlers(ioPortDispatcher);
        _wallClock.Start();
    }

    /// <summary>
    /// The sound channel used for rendering audio.
    /// </summary>
    public SoundChannel SoundChannel => _soundChannel;
    /// <summary>
    /// Last selected register
    /// </summary>
    public OplTimer Timer0 { get; private set; }

    public OplTimer Timer1 { get; private set; }

    /// <summary>
    /// The measure of wall clock time since the emulator started, in milliseconds.
    /// </summary>
    private double EmulatorRunTime => _wallClock.ElapsedMilliseconds;

    /// <inheritdoc />
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Read the current timer state
    /// </summary>
    public byte Read() {
        double time = EmulatorRunTime;
        byte ret = 0;

        // Overflow won't be set if a channel is masked
        if (Timer0.Update(time)) {
            ret |= 0x40;
            ret |= 0x80;
        }
        if (Timer1.Update(time)) {
            ret |= 0x20;
            ret |= 0x80;
        }
        return ret;
    }

    /// <inheritdoc />
    public override byte ReadByte(ushort port) {
        // Simulate a slight delay
        // In DOSBox this is done with CPU cycles, here we'll just make sure playback is initialized
        InitializePlaybackIfNeeded();

        // For OPL2 mode, we only respond to even ports (status register)
        if ((port & 1) == 0) {
            // Read timer status
            byte result = Read();

            // For OPL2 mode, the low bits must be 6 to indicate OPL2 presence
            if (_oplMode == OplMode.Opl2) {
                result |= 0x06;
            }

            return result;
        }
        // Check for AdLib Gold specific ports
        else if (_oplMode == OplMode.Opl3Gold) {
            if (port == 0x38A) {
                // Control status, not busy
                return 0;
            } else if (port == 0x38B && _ctrl.IsActive) {
                return AdlibGoldControlRead();
            }
        }

        // All other ports return 0xFF
        return 0xFF;
    }

    /// <inheritdoc />
    public override ushort ReadWord(ushort port) {
        // For most operations, we'll read byte by byte
        byte low = ReadByte(port);
        byte high = ReadByte((ushort)(port + 1));
        return (ushort)((high << 8) | low);
    }

    /// <summary>
    /// Check for it being a write to the timer
    /// </summary>
    public bool Write(byte reg, byte val) {
        switch (reg) {
            case 0x02:
                Timer0.Update(EmulatorRunTime);
                Timer0.SetCounter(val);
                return true;
            case 0x03:
                Timer1.Update(EmulatorRunTime);
                Timer1.SetCounter(val);
                return true;
            case 0x04:
                // Reset overflow in both timers
                if ((val & 0x80) > 0) {
                    Timer0.Reset();
                    Timer1.Reset();
                } else {
                    double time = EmulatorRunTime;
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

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        InitializePlaybackIfNeeded();

        // Special handling for AdLib Gold
        if (_oplMode == OplMode.Opl3Gold) {
            if (port == 0x38A) {
                if (value == 0xFF) {
                    _ctrl.IsActive = true;
                    return;
                } else if (value == 0xFE) {
                    _ctrl.IsActive = false;
                    return;
                } else if (_ctrl.IsActive) {
                    _ctrl.Index = value;
                    return;
                }
            } else if (port == 0x38B && _ctrl.IsActive) {
                AdlibGoldControlWrite(value);
                return;
            }
        }

        if ((port & 1) != 0) {
            // Odd port - write data to register
            if (!Write(_reg.normal, value)) {
                // If it's not a timer register write, then write to the OPL chip register
                WriteReg(_reg.normal, value);

                // Store in cache
                if (_reg.normal < 256) {
                    _cache[_reg.normal] = value;
                }
            }
        } else {
            // Even port - select register
            _reg.normal = value;

            // For OPL2 mode, only use the lower 8 bits for register address
            if (_oplMode == OplMode.Opl2) {
                _reg.normal &= 0xFF;
            }

            // Pass the command value onto the GUS if this is the AdLib command port
            if (port == 0x388) {
                // In a real implementation, we'd call:
                // GUS_MirrorAdLibCommandPortWrite(port, value);
            }
        }
    }

    /// <inheritdoc />
    public override void WriteWord(ushort port, ushort value) {
        // Break it down into byte operations
        WriteByte(port, (byte)(value & 0xFF));
        WriteByte((ushort)(port + 1), (byte)(value >> 8));
    }

    private byte AdlibGoldControlRead() {
        return _ctrl.Index switch {
            // Board Options
            0x00 => 0x50,// 16-bit ISA, surround module, no
                         // telephone/CDROM
                         // return 0x70; // 16-bit ISA, no
                         // telephone/surround/CD-ROM
                         // Left FM Volume
            0x09 => _ctrl.LVol,
            // Right FM Volume
            0x0a => _ctrl.RVol,
            // Audio Relocation
            0x15 => 0x388 >> 3,// Cryo installer detection
            _ => 0xff,
        };
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
                    // Dune CD version uses 32 volume steps in an apparent mistake, should be 128
                    float leftVol = (_ctrl.LVol & 0x1f) / 31.0f;
                    float rightVol = (_ctrl.RVol & 0x1f) / 31.0f;
                    _soundChannel.SetVolume(leftVol, rightVol);
                }
                break;
            case 0x18: // Surround
                _adlibGold.SurroundControlWrite(val);
                break;
        }
    }

    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _deviceThread.Dispose();
                lock (_audioQueue) {
                    _audioQueue.Clear();
                }
            }
            _disposed = true;
        }
    }

    private void InitializePlaybackIfNeeded() {
        if (!_deviceThread.Active) {
            _deviceThread.StartThreadIfNeeded();
        }
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(0x388, this);
        ioPortDispatcher.AddIOPortHandler(0x389, this);
        ioPortDispatcher.AddIOPortHandler(0x38A, this); // For AdLib Gold
        ioPortDispatcher.AddIOPortHandler(0x38B, this);

        if (_dualOpl) {
            //Read/Write
            ioPortDispatcher.AddIOPortHandler(0x220, this);
            ioPortDispatcher.AddIOPortHandler(0x221, this);
            ioPortDispatcher.AddIOPortHandler(0x222, this);
            ioPortDispatcher.AddIOPortHandler(0x223, this);
        }
        //Read/Write
        ioPortDispatcher.AddIOPortHandler(0x228, this);
        //Write
        ioPortDispatcher.AddIOPortHandler(0x229, this);
    }
    /// <summary>
    /// Generates and plays back output waveform data.
    /// </summary>
    private void PlaybackLoopBody() {
        float[]? frame = null;

        lock (_audioQueue) {
            if (_audioQueue.Count > 0) {
                frame = _audioQueue.Dequeue();
            }
        }

        if (frame != null) {
            // Render the frame to the sound channel
            _soundChannel.Render(frame);
        } else {
            // If no frames are available, generate one
            float[] newFrame = RenderFrame();
            _soundChannel.Render(newFrame);

            // Update the last rendered time
            _lastRenderedMs = EmulatorRunTime;
        }
    }

    private float[] RenderFrame() {
        // Generate a stereo audio frame
        short[] buffer = new short[2];

        // Generate OPL2 audio
        Opl3Nuked.Opl3GenerateStream(ref _oplChip, buffer, 1);

        // Process with AdLib Gold if needed
        if (_oplMode == OplMode.Opl3Gold && _adlibGold != null) {
            float[] output = new float[2];
            Span<short> input = buffer;
            Span<float> outputSpan = output;
            _adlibGold.Process(input, 1, outputSpan);
            return output;
        }

        // Convert to float in range [-1, 1]
        return new float[] {
                buffer[0] / 32768.0f,
                buffer[1] / 32768.0f
            };
        // Return silence if OPL chip isn't available
        return new float[] { 0.0f, 0.0f };
    }

    /// <summary>
    /// Writes a value to an OPL register
    /// </summary>
    private void WriteReg(byte reg, byte val) {
        // Update the OPL chip with the new register value
        Opl3Nuked.Opl3WriteRegBuffered(ref _oplChip, reg, val);

        // For OPL3 mode, check if we're changing the OPL3 mode bit
        if (reg == 0x105) {
            _newm = (val & 0x01) != 0;
        }
    }
    private struct Reg {
        public byte[] dual;
        public byte normal;
        public Reg() {
            normal = 0;
            dual = new byte[2];
        }
    }
}

public class OplTimer {
    /// <summary>
    /// Clock Interval in Milliseconds
    /// </summary>
    private readonly double _clockInterval = 0.0;

    private byte _counter = 0;

    /// <summary>
    /// Cycle interval
    /// </summary>
    private double _counterInterval = 0.0;

    private bool _enabled = false;

    private bool _masked = false;

    private bool _overflow = false;

    /// <summary>
    /// Rounded down start time
    /// </summary>
    private double _start = 0.0;

    /// <summary>
    /// Time when you overflow
    /// </summary>
    private double _trigger = 0.0;
    public OplTimer(short micros) {
        _clockInterval = micros * 0.001;
        SetCounter(0);
    }

    public void Reset() {
        // On a reset make sure the start is in sync with the next cycle
        _overflow = false;
    }

    public void SetCounter(byte val) {
        _counter = val;
        // Interval for next cycle
        _counterInterval = (256 - _counter) * _clockInterval;
    }
    public void SetMask(bool set) {
        _masked = set;
        if (_masked) {
            _overflow = false;
        }
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

    public void Stop() {
        _enabled = false;
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
public static class SoundChannelExtensions {
    public static void SetVolume(this SoundChannel channel, float left, float right) {
        channel.Volume = (int)((left + right) * 50); // Average and scale to percentage
    }
}