// SPDX-License-Identifier: GPL-3.0-or-later

namespace Spice86.Core.Emulator.Devices.Sound;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Libs.Sound.Filters.IirFilters.Filters.RBJ;
using Spice86.Shared.Interfaces;

/// <summary>
///     PC speaker device ported from DOSBox Staging's impulse model.
/// </summary>
public sealed class PcSpeaker : DefaultIOPortHandler, IDisposable, IPitSpeaker {
    private const int PcSpeakerPortNumber = 0x61;

    private const float PwmScalar = 0.5f;
    private const float NeutralAmplitude = 0.0f;
    private const float SincAmplitudeFade = 0.999f;
    private const float CutoffMargin = 0.2f;
    private const double FilterQ = 0.7071;

    private const int SampleRateHz = 32000;
    private const int FramesPerBuffer = 512;
    private const int Channels = 2;
    private const int SampleRatePerMillisecond = SampleRateHz / 1000;
    private const int SincFilterQuality = 100;
    private const int SincOversamplingFactor = 32;
    private const int SincFilterWidth = SincFilterQuality * SincOversamplingFactor;
    private const int WaveformSize = SincFilterQuality + SampleRatePerMillisecond;
    private const int MaxBufferedFrames = FramesPerBuffer * 2;

    private const float PositiveAmplitude = short.MaxValue * PwmScalar;
    private const float NegativeAmplitude = -PositiveAmplitude;
    private const float MsPerPitTick = 1000.0f / PitTimer.PitTickRate;
    private static readonly int MinimumCounter = Math.Max(1, 2 * PitTimer.PitTickRate / SampleRateHz);
    private readonly float[] _audioBuffer = new float[FramesPerBuffer * Channels];
    private readonly DeviceThread _deviceThread;
    private readonly DualPic _dualPic;
    private readonly float[] _frameBuffer = new float[FramesPerBuffer];
    private readonly HighPass _highPassFilter = new();
    private readonly float[] _impulseLookup = new float[SincFilterWidth];
    private readonly ILoggerService _logger;
    private readonly LowPass _lowPassFilter = new();
    private readonly object _outputLock = new();
    private readonly Queue<float> _outputQueue = new();

    private readonly PitState _pit = new();

    private readonly SoundChannel _soundChannel;
    private readonly TimerTickHandler _tickHandler;
    private readonly float[] _waveform = new float[WaveformSize];
    private float _accumulator;
    private bool _disposed;

    private float _frameCounter;
    private IPitControl? _pitControl;
    private PpiPortB _portB;
    private PpiPortB _previousPortB;
    private int _tallyOfSilence;
    private int _waveformHead;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PcSpeaker" /> class.
    /// </summary>
    /// <param name="softwareMixer">The shared software mixer used to render output.</param>
    /// <param name="state">Machine state for registering I/O handlers.</param>
    /// <param name="ioPortDispatcher">Dispatcher used to register the speaker port.</param>
    /// <param name="pauseHandler">Handler used to honor emulator pause requests.</param>
    /// <param name="loggerService">Logger used for diagnostics.</param>
    /// <param name="dualPic">Programmable interrupt controllers used for timing callbacks.</param>
    /// <param name="failOnUnhandledPort">Indicates whether unhandled port accesses should throw.</param>
    /// <param name="pitControl">Optional PIT control used to synchronize channel 2 output.</param>
    public PcSpeaker(
        SoftwareMixer softwareMixer,
        State state,
        IOPortDispatcher ioPortDispatcher,
        IPauseHandler pauseHandler,
        ILoggerService loggerService,
        DualPic dualPic,
        bool failOnUnhandledPort,
        IPitControl? pitControl = null)
        : base(state, failOnUnhandledPort, loggerService) {
        _logger = loggerService;
        _dualPic = dualPic;
        _pitControl = pitControl;

        _soundChannel = softwareMixer.CreateChannel(nameof(PcSpeaker), SampleRateHz);
        _soundChannel.Volume = 100;
        _soundChannel.StereoSeparation = 0;

        _highPassFilter.Setup(SampleRateHz, 120, FilterQ);
        _lowPassFilter.Setup(SampleRateHz, 4300, FilterQ);

        InitializeImpulseLookup();
        InitializePitState();

        ioPortDispatcher.AddIOPortHandler(PcSpeakerPortNumber, this);

        _tickHandler = OnPicTick;
        _dualPic.AddTickHandler(_tickHandler);

        _deviceThread = new DeviceThread(nameof(PcSpeaker), PlaybackLoop, pauseHandler, loggerService);
    }

    /// <inheritdoc />
    public void Dispose() {
        Dispose(true);
    }

    /// <summary>
    ///     Configures channel 2 of the PIT for the PC speaker using the supplied counter value and mode.
    /// </summary>
    /// <param name="count">The PIT reload value.</param>
    /// <param name="mode">The PIT operating mode that should be used.</param>
    public void SetCounter(int count, PitMode mode) {
        _logger.Debug("PCSPEAKER: Configuring counter with value {Count} in mode {Mode}", count, mode);

        float newIndex = GetPicTickIndex();
        float durationMs = MsPerPitTick * count;

        ForwardPit(newIndex);

        switch (mode) {
            case PitMode.InterruptOnTerminalCount:
                _pit.Index = 0.0f;
                _pit.Amplitude = NegativeAmplitude;
                _pit.MaxMilliseconds = durationMs;
                AddPitOutput(newIndex);
                break;

            case PitMode.OneShot:
                _pit.Mode1PendingMax = durationMs;
                if (_pit.Mode1WaitingForCounter) {
                    _pit.Mode1WaitingForCounter = false;
                    _pit.Mode1WaitingForTrigger = true;
                }

                break;

            case PitMode.RateGenerator:
            case PitMode.RateGeneratorAlias:
                _pit.Index = 0.0f;
                _pit.Amplitude = NegativeAmplitude;
                AddPitOutput(newIndex);
                _pit.MaxMilliseconds = durationMs;
                _pit.HalfMilliseconds = MsPerPitTick;
                break;

            case PitMode.SquareWave:
            case PitMode.SquareWaveAlias:
                if (count < MinimumCounter) {
                    _logger.Debug(
                        "PCSPEAKER: Counter value {Count} below minimum {Minimum}; forcing speaker inactive.",
                        count, MinimumCounter);
                    _pit.Amplitude = PositiveAmplitude;
                    _pit.Mode = PitMode.Inactive;
                    AddPitOutput(newIndex);
                    return;
                }

                _pit.NewMaxMilliseconds = durationMs;
                _pit.NewHalfMilliseconds = _pit.NewMaxMilliseconds / 2.0f;
                _logger.Debug(
                    "PCSPEAKER: Square wave period set to {Period} ms with half-period {HalfPeriod} ms.",
                    _pit.NewMaxMilliseconds, _pit.NewHalfMilliseconds);
                if (!_pit.Mode3Counting) {
                    _pit.Index = 0.0f;
                    _pit.MaxMilliseconds = _pit.NewMaxMilliseconds;
                    _pit.HalfMilliseconds = _pit.NewHalfMilliseconds;
                    if (_previousPortB.Timer2Gating) {
                        _pit.Mode3Counting = true;
                        _pit.Amplitude = PositiveAmplitude;
                        AddPitOutput(newIndex);
                    }
                }

                break;

            case PitMode.SoftwareStrobe:
                _pit.Amplitude = PositiveAmplitude;
                AddPitOutput(newIndex);
                _pit.Index = 0.0f;
                _pit.MaxMilliseconds = durationMs;
                break;

            default:
                _logger.Warning("PCSPEAKER: Unhandled speaker PIT mode {Mode} with count {Count}", mode, count);
                return;
        }

        _pit.Mode = mode;
    }

    /// <summary>
    ///     Updates the speaker control state based on PIT mode transitions.
    /// </summary>
    /// <param name="mode">The PIT mode that should now be active for the speaker.</param>
    public void SetPitControl(PitMode mode) {
        _logger.Debug("PCSPEAKER: Updating PIT control for mode {Mode}", mode);

        float newIndex = GetPicTickIndex();
        ForwardPit(newIndex);

        switch (mode) {
            case PitMode.OneShot:
                _pit.Mode = mode;
                _pit.Amplitude = PositiveAmplitude;
                _pit.Mode1WaitingForCounter = true;
                _pit.Mode1WaitingForTrigger = false;
                AddPitOutput(newIndex);
                break;

            case PitMode.SquareWave:
            case PitMode.SquareWaveAlias:
                _pit.Mode = mode;
                _pit.Amplitude = PositiveAmplitude;
                _pit.Mode3Counting = false;
                AddPitOutput(newIndex);
                break;

            default:
                _logger.Warning("PCSPEAKER: Unsupported PIT control mode {Mode}", mode);
                break;
        }
    }

    /// <summary>
    ///     Associates a PIT control with the speaker so gate transitions can be observed.
    /// </summary>
    /// <param name="pitControl">The PIT control instance to attach.</param>
    public void AttachPitControl(IPitControl pitControl) {
        _pitControl = pitControl;
        if (_logger.IsEnabled(LogEventLevel.Information)) {
            _logger.Debug("PCSPEAKER: PIT control {PitControlType} attached.", pitControl.GetType().Name);
        }
    }

    /// <inheritdoc />
    public override byte ReadByte(ushort port) {
        if (port != PcSpeakerPortNumber) {
            return base.ReadByte(port);
        }

        _portB.ReadToggle = !_portB.ReadToggle;

        _portB.Timer2GatingAlias = _pitControl?.IsChannel2OutputHigh() == true;

        return _portB.Data;
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        if (port != PcSpeakerPortNumber) {
            base.WriteByte(port, value);
            return;
        }

        PpiPortB newPortB = _portB;
        newPortB.Data = value;

        bool outputChanged = newPortB.Timer2GatingAndSpeakerOut != _portB.Timer2GatingAndSpeakerOut;
        bool timerChanged = newPortB.Timer2Gating != _portB.Timer2Gating;

        _portB = newPortB;

        if (_portB.XtClearKeyboard) {
            _portB.XtClearKeyboard = false;
        }

        if (!outputChanged) {
            return;
        }

        if (timerChanged) {
            _pitControl?.SetGate2(_portB.Timer2Gating);
        }

        SetType(_portB);
    }

    private void Dispose(bool disposing) {
        if (_disposed) {
            return;
        }

        if (disposing) {
            _dualPic.RemoveTickHandler(_tickHandler);
            _deviceThread.Dispose();
        }

        _disposed = true;
    }

    private void OnPicTick() {
        _frameCounter += SampleRatePerMillisecond;
        int requestedFrames = (int)Math.Floor(_frameCounter);
        _frameCounter -= requestedFrames;

        if (requestedFrames <= 0) {
            return;
        }

        PicCallback(requestedFrames);
    }

    private void PicCallback(int requestedFrames) {
        if (requestedFrames <= 0) {
            return;
        }

        ForwardPit(1.0f);
        _pit.LastIndex = 0.0f;

        int remainingFrames = requestedFrames;

        while (remainingFrames > 0 && _waveform.Length > 0) {
            float value = PopWaveformSample();
            _accumulator += value;
            EnqueueSample(_accumulator);
            remainingFrames--;

            _tallyOfSilence = Math.Abs(_accumulator) > 1.0f ? 0 : _tallyOfSilence + 1;
            _accumulator *= SincAmplitudeFade;
        }

        if (remainingFrames > 0) {
            _pit.PreviousAmplitude = NeutralAmplitude;
        }

        while (remainingFrames > 0) {
            EnqueueSample(NeutralAmplitude);
            _tallyOfSilence++;
            remainingFrames--;
        }
    }

    private void SetType(PpiPortB newPortB) {
        float newIndex = GetPicTickIndex();
        ForwardPit(newIndex);

        bool pitTrigger = !_previousPortB.Timer2Gating && newPortB.Timer2Gating;
        _previousPortB.Data = newPortB.Data;

        if (pitTrigger) {
            switch (_pit.Mode) {
                case PitMode.OneShot:
                    if (_pit.Mode1WaitingForCounter) {
                        break;
                    }

                    _pit.Amplitude = NegativeAmplitude;
                    _pit.Index = 0.0f;
                    _pit.MaxMilliseconds = _pit.Mode1PendingMax;
                    _pit.Mode1WaitingForTrigger = false;
                    break;

                case PitMode.SquareWave:
                case PitMode.SquareWaveAlias:
                    _pit.Mode3Counting = true;
                    _pit.Index = 0.0f;
                    _pit.MaxMilliseconds = _pit.NewMaxMilliseconds;
                    _pit.NewHalfMilliseconds = _pit.NewMaxMilliseconds / 2.0f;
                    _pit.HalfMilliseconds = _pit.NewHalfMilliseconds;
                    _pit.Amplitude = PositiveAmplitude;
                    break;
            }
        } else if (!newPortB.Timer2Gating && _pit.Mode is PitMode.SquareWave or PitMode.SquareWaveAlias) {
            _pit.Amplitude = PositiveAmplitude;
            _pit.Mode3Counting = false;
        }

        AddImpulse(newIndex, newPortB.SpeakerOutput ? _pit.Amplitude : NegativeAmplitude);
    }

    private void AddPitOutput(float index) {
        if (_previousPortB.SpeakerOutput) {
            AddImpulse(index, _pit.Amplitude);
        }
    }

    private void ForwardPit(float newIndex) {
        float passed = newIndex - _pit.LastIndex;
        float delayBase = _pit.LastIndex;
        _pit.LastIndex = newIndex;

        switch (_pit.Mode) {
            case PitMode.Inactive:
                return;

            case PitMode.InterruptOnTerminalCount:
                if (_pit.Index >= _pit.MaxMilliseconds) {
                    return;
                }

                _pit.Index += passed;
                if (_pit.Index >= _pit.MaxMilliseconds) {
                    float delay = delayBase + _pit.MaxMilliseconds - _pit.Index + passed;
                    _pit.Amplitude = PositiveAmplitude;
                    AddPitOutput(delay);
                }

                return;

            case PitMode.OneShot:
                if (_pit.Mode1WaitingForCounter || _pit.Mode1WaitingForTrigger) {
                    return;
                }

                if (_pit.Index >= _pit.MaxMilliseconds) {
                    return;
                }

                _pit.Index += passed;
                if (_pit.Index >= _pit.MaxMilliseconds) {
                    float delay = delayBase + _pit.MaxMilliseconds - _pit.Index + passed;
                    _pit.Amplitude = PositiveAmplitude;
                    AddPitOutput(delay);
                    _pit.Mode1WaitingForTrigger = true;
                }

                return;

            case PitMode.RateGenerator:
            case PitMode.RateGeneratorAlias:
                while (passed > 0.0f) {
                    if (_pit.Index >= _pit.HalfMilliseconds) {
                        if (_pit.Index + passed >= _pit.MaxMilliseconds) {
                            float delay = _pit.MaxMilliseconds - _pit.Index;
                            delayBase += delay;
                            passed -= delay;
                            _pit.Amplitude = NegativeAmplitude;
                            AddPitOutput(delayBase);
                            _pit.Index = 0.0f;
                        } else {
                            _pit.Index += passed;
                            return;
                        }
                    } else {
                        if (_pit.Index + passed >= _pit.HalfMilliseconds) {
                            float delay = _pit.HalfMilliseconds - _pit.Index;
                            delayBase += delay;
                            passed -= delay;
                            _pit.Amplitude = PositiveAmplitude;
                            AddPitOutput(delayBase);
                            _pit.Index = _pit.HalfMilliseconds;
                        } else {
                            _pit.Index += passed;
                            return;
                        }
                    }
                }

                break;

            case PitMode.SquareWave:
            case PitMode.SquareWaveAlias:
                if (!_pit.Mode3Counting) {
                    break;
                }

                while (passed > 0.0f) {
                    if (_pit.Index >= _pit.HalfMilliseconds) {
                        if (_pit.Index + passed >= _pit.MaxMilliseconds) {
                            float delay = _pit.MaxMilliseconds - _pit.Index;
                            delayBase += delay;
                            passed -= delay;
                            _pit.Amplitude = PositiveAmplitude;
                            AddPitOutput(delayBase);
                            _pit.Index = 0.0f;
                            _pit.MaxMilliseconds = _pit.NewMaxMilliseconds;
                            _pit.HalfMilliseconds = _pit.NewHalfMilliseconds;
                        } else {
                            _pit.Index += passed;
                            return;
                        }
                    } else {
                        if (_pit.Index + passed >= _pit.HalfMilliseconds) {
                            float delay = _pit.HalfMilliseconds - _pit.Index;
                            delayBase += delay;
                            passed -= delay;
                            _pit.Amplitude = NegativeAmplitude;
                            AddPitOutput(delayBase);
                            _pit.Index = _pit.HalfMilliseconds;
                            _pit.MaxMilliseconds = _pit.NewMaxMilliseconds;
                            _pit.HalfMilliseconds = _pit.NewHalfMilliseconds;
                        } else {
                            _pit.Index += passed;
                            return;
                        }
                    }
                }

                break;

            case PitMode.SoftwareStrobe:
                if (_pit.Index < _pit.MaxMilliseconds && _pit.Index + passed >= _pit.MaxMilliseconds) {
                    float delay = _pit.MaxMilliseconds - _pit.Index;
                    delayBase += delay;
                    _pit.Amplitude = NegativeAmplitude;
                    AddPitOutput(delayBase);
                    _pit.Index = _pit.MaxMilliseconds;
                } else {
                    _pit.Index += passed;
                }

                break;
        }
    }

    private void AddImpulse(float index, float amplitude) {
        if (!_deviceThread.Active) {
            _pit.PreviousAmplitude = NeutralAmplitude;
        }

        if (Math.Abs(amplitude - _pit.PreviousAmplitude) < 1e-6f) {
            return;
        }

        _pit.PreviousAmplitude = amplitude;

        index = Math.Clamp(index, 0.0f, 1.0f);

        float samplesInImpulse = index * SampleRatePerMillisecond;
        int phase = (int)(samplesInImpulse * SincOversamplingFactor) % SincOversamplingFactor;
        int offset = (int)samplesInImpulse;

        if (phase != 0) {
            offset++;
            phase = SincOversamplingFactor - phase;
        }

        for (int i = 0; i < SincFilterQuality; i++) {
            int waveformIndex = offset + i;
            int impulseIndex = phase + (i * SincOversamplingFactor);
            float value = amplitude * _impulseLookup[impulseIndex];
            AccumulateWaveform(waveformIndex, value);
        }
    }

    private void AccumulateWaveform(int index, float value) {
        if (index < 0 || index >= _waveform.Length) {
            _logger.Warning("PCSPEAKER: Waveform accumulation index {Index} outside buffer length {Length}", index,
                _waveform.Length);
            return;
        }

        int actualIndex = (_waveformHead + index) % _waveform.Length;
        _waveform[actualIndex] += value;
    }

    private float PopWaveformSample() {
        float value = _waveform[_waveformHead];
        _waveform[_waveformHead] = 0.0f;
        _waveformHead++;
        if (_waveformHead >= _waveform.Length) {
            _waveformHead = 0;
        }

        return value;
    }

    private void EnqueueSample(float value) {
        lock (_outputLock) {
            if (_outputQueue.Count >= MaxBufferedFrames) {
                _outputQueue.Dequeue();
            }

            _outputQueue.Enqueue(value);
        }

        _deviceThread.StartThreadIfNeeded();
    }

    private void PlaybackLoop() {
        while (true) {
            int framesToProcess;
            lock (_outputLock) {
                framesToProcess = Math.Min(_outputQueue.Count, FramesPerBuffer);
                if (_outputQueue.Count < FramesPerBuffer) {
                    break;
                }

                for (int i = 0; i < framesToProcess; i++) {
                    _frameBuffer[i] = _outputQueue.Dequeue();
                }
            }

            if (framesToProcess == 0) {
                break;
            }

            ApplyFiltersAndRender(framesToProcess);
        }
    }

    private void ApplyFiltersAndRender(int frames) {
        for (int i = 0; i < frames; i++) {
            _frameBuffer[i] = _highPassFilter.Filter(_frameBuffer[i]);
        }

        for (int i = 0; i < frames; i++) {
            _frameBuffer[i] = _lowPassFilter.Filter(_frameBuffer[i]);
        }

        int sampleIndex = 0;
        for (int i = 0; i < frames; i++) {
            float sample = Math.Clamp(_frameBuffer[i] / short.MaxValue, -1.0f, 1.0f);
            _audioBuffer[sampleIndex++] = sample;
            _audioBuffer[sampleIndex++] = sample;
        }

        _soundChannel.Render(_audioBuffer.AsSpan(0, frames * Channels));
    }

    private float GetPicTickIndex() {
        double full = _dualPic.GetFullIndex();
        return (float)(full - Math.Floor(full));
    }

    private void InitializeImpulseLookup() {
        const double factor = SampleRateHz * SincOversamplingFactor;
        for (int i = 0; i < SincFilterWidth; i++) {
            double time = i / factor;
            _impulseLookup[i] = CalculateImpulse(time);
        }
    }

    private static float CalculateImpulse(double t) {
        if (t <= 0.0 || t * SampleRateHz >= SincFilterQuality) {
            return 0.0f;
        }

        const double factor = SincFilterQuality / (2.0 * SampleRateHz);
        double window = 1.0 + Math.Cos(2.0 * SampleRateHz * Math.PI * (factor - t) / SincFilterQuality);
        const double fc = SampleRateHz / (2.0 + CutoffMargin);
        double amplitude = window * Sinc(2.0 * fc * Math.PI * (t - factor)) / 2.0;
        return (float)amplitude;
    }

    private static double Sinc(double t) {
        double result = 1.0;
        const int sincAccuracy = 20;
        for (int k = 1; k < sincAccuracy; k++) {
            result *= Math.Cos(t / Math.Pow(2.0, k));
        }

        return result;
    }

    private void InitializePitState() {
        _pit.MaxMilliseconds = 1320000.0f / PitTimer.PitTickRate;
        _pit.NewMaxMilliseconds = _pit.MaxMilliseconds;
        _pit.HalfMilliseconds = _pit.MaxMilliseconds / 2.0f;
        _pit.NewHalfMilliseconds = _pit.HalfMilliseconds;
        _pit.Mode = PitMode.SquareWave;
        _pit.Amplitude = PositiveAmplitude;
        _pit.PreviousAmplitude = NegativeAmplitude;
        _pit.Mode1WaitingForTrigger = true;
    }

    private sealed class PitState {
        public float Amplitude;
        public float HalfMilliseconds;
        public float Index;
        public float LastIndex;
        public float MaxMilliseconds;
        public PitMode Mode;
        public float Mode1PendingMax;
        public bool Mode1WaitingForCounter;
        public bool Mode1WaitingForTrigger = true;
        public bool Mode3Counting;
        public float NewHalfMilliseconds;
        public float NewMaxMilliseconds;
        public float PreviousAmplitude;
    }
}