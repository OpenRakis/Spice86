namespace Spice86.Core.Emulator.Devices.Sound.Opl;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.Devices.Sound.Ym7128b;
using Spice86.Shared.Interfaces;

/// <summary>
/// OPL2 FM synthesizer emulator using Nuked OPL2 core for precise hardware compatibility.
/// Focuses on single OPL2 chip emulation (YM3812) as used in Sound Blaster and AdLib cards.
/// </summary>
public class OPL2 : DefaultIOPortHandler, IDisposable {
    private const int OplSampleRateHz = 49716; // OPL2 native rate
    private const float OplVolumeGain = 1.5f;  // DOSBox OPL gain

    private readonly SoundChannel _soundChannel;
    private readonly DeviceThread _deviceThread;
    private readonly IPerformanceMeasureReader _performanceMeasurer;

    // Nuked OPL2 core using YM7128B implementation
    private ChipIdeal _nukedChip = new();
    private ChipIdealProcessData _processData = new();

    // OPL2 state
    private ushort _currentRegister;
    private readonly byte[] _registerCache = new byte[256]; // OPL2 has 256 registers

    // Audio rendering using Span<T> for performance
    private readonly Queue<(float Left, float Right)> _fifo = new();
    private double _lastRenderedMs;
    private readonly double _msPerFrame;

    // Timer implementation (from DOSBox)
    private readonly Timer _timer0;
    private readonly Timer _timer1;

    // DOSBox DC bias removal
    private bool _removeDcBias;
    private readonly DcBiasRemover _dcBiasRemover = new();

    private readonly float[] _playBuffer = new float[1024 * 2]; // stereo
    private bool _disposed;

    /// <summary>
    /// Gets the sound channel for this OPL2 instance.
    /// </summary>
    public SoundChannel SoundChannel => _soundChannel;

    /// <summary>
    /// Initializes a new OPL2 instance.
    /// </summary>
    public OPL2(SoundChannel soundChannel, State state, IPerformanceMeasureReader cpuPerfMeasurer,
        IOPortDispatcher ioPortDispatcher, bool failOnUnhandledPort,
        ILoggerService loggerService, IPauseHandler pauseHandler)
        : base(state, failOnUnhandledPort, loggerService) {

        _performanceMeasurer = cpuPerfMeasurer;
        _soundChannel = soundChannel;
        _timer0 = new Timer(80);   // 80 microseconds - Timer 0
        _timer1 = new Timer(320);  // 320 microseconds - Timer 1
        _msPerFrame = 1000.0 / OplSampleRateHz;

        // Configure the sound channel for OPL2
        _soundChannel.SetSampleRate(OplSampleRateHz);
        _soundChannel.Set0dbScalar(OplVolumeGain);

        // Configure noise gate for OPL2 residual noise (DOSBox values)
        float thresholdDb = -65.0f + GainToDecibel(OplVolumeGain);
        _soundChannel.ConfigureNoiseGate(thresholdDb, 1.0f, 100.0f);
        _soundChannel.EnableNoiseGate(true);

        _deviceThread = new DeviceThread(nameof(OPL2), PlaybackLoopBody,
            pauseHandler, loggerService);

        InitPortHandlers(ioPortDispatcher);
        InitializeOpl2();
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        // OPL2 standard ports (AdLib compatible)
        ioPortDispatcher.AddIOPortHandler(0x388, this); // Address/Status
        ioPortDispatcher.AddIOPortHandler(0x389, this); // Data

        // Sound Blaster ports for OPL2
        ioPortDispatcher.AddIOPortHandler(0x228, this); // Left OPL2 Address
        ioPortDispatcher.AddIOPortHandler(0x229, this); // Left OPL2 Data
    }

    private void InitializeOpl2() {
        // Initialize Nuked OPL2 using YM7128B implementation
        Ym7128B.ChipIdealReset(ref _nukedChip);
        Ym7128B.ChipIdealSetup(ref _nukedChip, OplSampleRateHz);
        Ym7128B.ChipIdealStart(ref _nukedChip);

        // Initialize OPL2 tone generators per DOSBox Adlib v1.51 driver values
        // Games like Crystals of Arborea, Transarctica, Ishar series rely on this initialization

        // First 9 operators for 4-op style initialization (but OPL2 only supports 2-op)
        for (int op = 0; op < 9; op++) {
            WriteOpl2Register((ushort)(0x20 + op), 0x01); // MULT=1
            WriteOpl2Register((ushort)(0x40 + op), 0x1F); // KSL=1, TL=15
            WriteOpl2Register((ushort)(0x60 + op), 0xF0); // AR=15, DR=0
            WriteOpl2Register((ushort)(0x80 + op), 0x77); // SL=7, RR=7
        }

        // Remaining 9 operators (modulator setup)
        for (int op = 9; op < 18; op++) {
            WriteOpl2Register((ushort)(0x20 + op), 0x01); // MULT=1
            WriteOpl2Register((ushort)(0x60 + op), 0xF0); // AR=15, DR=0
            WriteOpl2Register((ushort)(0x80 + op), 0x42); // SL=4, RR=2
        }
    }

    /// <inheritdoc />
    public override byte ReadByte(ushort port) {
        long time = Environment.TickCount64;
        byte ret = 0;

        // OPL2 timer status (matches DOSBox OPL2 behavior)
        if (_timer0.Update(time)) {
            ret |= 0x40 | 0x80; // Timer 0 overflow + busy flag
        }
        if (_timer1.Update(time)) {
            ret |= 0x20 | 0x80; // Timer 1 overflow + busy flag  
        }

        // OPL2 specific: low bits are always 6
        return (byte)(ret | 0x6);
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        RenderUpToNow();

        if (!_deviceThread.Active) {
            InitializePlaybackIfNeeded();
        }

        if ((port & 1) == 0) {
            // Address port
            _currentRegister = (ushort)(value & 0xFF); // OPL2 only uses 8-bit addresses
        } else {
            // Data port
            if (!WriteOpl2Timer(_currentRegister, value)) {
                WriteOpl2Register(_currentRegister, value);
                CacheWrite(_currentRegister, value);
            }
        }
    }

    private void InitializePlaybackIfNeeded() {
        if (!_deviceThread.Active) {
            FillBuffer(_playBuffer);
            _deviceThread.StartThreadIfNeeded();
        }
    }

    private bool WriteOpl2Timer(ushort reg, byte val) {
        // Handle OPL2 timer registers (matches DOSBox implementation)
        switch (reg) {
            case 0x02:
                _timer0.Update(Environment.TickCount64);
                _timer0.SetCounter(val);
                return true;

            case 0x03:
                _timer1.Update(Environment.TickCount64);
                _timer1.SetCounter(val);
                return true;

            case 0x04:
                // Timer control
                if ((val & 0x80) != 0) {
                    _timer0.Reset();
                    _timer1.Reset();
                } else {
                    long time = Environment.TickCount64;
                    if ((val & 0x1) != 0) {
                        _timer0.Start(time);
                    } else {
                        _timer0.Stop();
                    }

                    if ((val & 0x2) != 0) {
                        _timer1.Start(time);
                    } else {
                        _timer1.Stop();
                    }

                    _timer0.SetMask((val & 0x40) > 0);
                    _timer1.SetMask((val & 0x20) > 0);
                }
                return true;
        }
        return false;
    }

    private void WriteOpl2Register(ushort reg, byte val) {
        // Write to OPL2 register via Nuked core using YM7128B implementation
        // This maps OPL2 registers to the YM7128B register space
        if (reg < (byte)Reg.Count) {
            Ym7128B.ChipIdealWrite(ref _nukedChip, (byte)reg, val);
        }
    }

    private void CacheWrite(ushort reg, byte val) {
        if (reg < _registerCache.Length) {
            _registerCache[reg] = val;
        }
    }

    private (float Left, float Right) RenderFrame() {
        // Set input (OPL2 generates, doesn't process external input)
        _processData.Inputs[(int)InputChannel.Mono] = 0.0f;

        // Generate frame using Nuked OPL2 via YM7128B implementation
        Ym7128B.ChipIdealProcess(ref _nukedChip, ref _processData);

        // Get stereo output
        float left = _processData.Outputs[(int)OutputChannel.Left];
        float right = _processData.Outputs[(int)OutputChannel.Right];

        // Apply DC bias removal if needed (DOSBox behavior)
        if (_removeDcBias) {
            short leftShort = (short)(left * 32767.0f);
            short rightShort = (short)(right * 32767.0f);
            leftShort = _dcBiasRemover.RemoveDcBias(leftShort, DcBiasRemover.Channel.Left);
            rightShort = _dcBiasRemover.RemoveDcBias(rightShort, DcBiasRemover.Channel.Right);
            left = leftShort / 32767.0f;
            right = rightShort / 32767.0f;
        }

        return (left, right);
    }

    private void RenderUpToNow() {
        // DOSBox equivalent: const auto now = PIC_FullIndex();
        double now = GetPicFullIndex();

        // Wake up the channel and update the last rendered time datum.
        if (_soundChannel.WakeUp()) {
            _lastRenderedMs = now;
            return;
        }
        
        // Keep rendering until we're current
        while (_lastRenderedMs < now) {
            _lastRenderedMs += _msPerFrame;
            _fifo.Enqueue(RenderFrame());
        }
    }

    // Equivalent to DOSBox PIC_FullIndex() using actual performance measurements
    private double GetPicFullIndex() {
        // Use the actual measured cycles per millisecond from the performance measurer
        // This gives us the real-time performance data like DOSBox's CPU_CycleMax
        var actualCyclesPerMs = _performanceMeasurer.ValuePerMillisecond;
        
        // Calculate whole milliseconds completed  
        long wholeMs = _state.Cycles / actualCyclesPerMs;
        
        // Calculate fractional progress within current millisecond
        // This is equivalent to DOSBox's PIC_TickIndex()
        double fractionalMs = (_state.Cycles % actualCyclesPerMs) / (double)actualCyclesPerMs;
        
        return wholeMs + fractionalMs;
    }

    /// <summary>
    /// Generates and plays back output waveform data.
    /// </summary>
    private void PlaybackLoopBody() {
        _soundChannel.Render(_playBuffer);
        FillBuffer(_playBuffer);
    }

    private void FillBuffer(Span<float> playBuffer) {
        int framesRequested = playBuffer.Length / 2; // stereo
        int framesRemaining = framesRequested;

        // First, send any frames we've queued since the last callback
        int bufferIndex = 0;
        while (framesRemaining > 0 && _fifo.Count > 0) {
            (float Left, float Right) frame = _fifo.Dequeue();
            playBuffer[bufferIndex++] = frame.Left;
            playBuffer[bufferIndex++] = frame.Right;
            framesRemaining--;
        }

        // If the queue's run dry, render the remainder
        while (framesRemaining > 0) {
            (float Left, float Right) frame = RenderFrame();
            playBuffer[bufferIndex++] = frame.Left;
            playBuffer[bufferIndex++] = frame.Right;
            framesRemaining--;
        }

        _lastRenderedMs = Environment.TickCount64;
    }

    /// <summary>
    /// Enables or disables DC bias removal.
    /// </summary>
    public void SetDcBiasRemoval(bool enabled) {
        _removeDcBias = enabled;
    }

    private static float GainToDecibel(float gain) {
        return 20.0f * (float)Math.Log10(gain);
    }

    public void Dispose() {
        if (_disposed) return;

        _deviceThread?.Dispose();
        _disposed = true;
    }

    /// <summary>
    /// DOSBox-compatible timer implementation.
    /// </summary>
    private class Timer {
        private readonly double _clockInterval;
        private double _counterInterval;
        private double _start;
        private double _trigger;
        private byte _counter;
        private bool _enabled;
        private bool _masked;
        private bool _overflow;

        public Timer(int micros) {
            _clockInterval = micros * 0.001; // Convert to milliseconds
            SetCounter(0);
        }

        public bool Update(double time) {
            if (_enabled && time >= _trigger) {
                double deltaTime = time - _trigger;
                double counterMod = deltaTime % _counterInterval;

                _start = time - counterMod;
                _trigger = _start + _counterInterval;

                if (!_masked) {
                    _overflow = true;
                }
            }
            return _overflow;
        }

        public void Reset() {
            _overflow = false;
        }

        public void SetCounter(byte val) {
            _counter = val;
            _counterInterval = (256 - _counter) * _clockInterval;
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
            if (!_enabled) {
                _enabled = true;
                _overflow = false;

                double clockMod = time % _clockInterval;
                _start = time - clockMod;
                _trigger = _start + _counterInterval;
            }
        }
    }
}