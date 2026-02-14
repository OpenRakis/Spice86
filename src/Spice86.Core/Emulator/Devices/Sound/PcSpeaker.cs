namespace Spice86.Core.Emulator.Devices.Sound;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Libs.Sound.Common;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Diagnostics;

/// <summary>
///     PC speaker device
/// </summary>
public class PcSpeaker : DefaultIOPortHandler, IPitSpeaker, IAudioQueueDevice<float>, IMixerQueueNotifier, IDisposable {
    private const int PcSpeakerPortNumber = 0x61;

    private const float PwmScalar = 0.5f;
    private const float NeutralAmplitude = 0.0f;
    private const float SincAmplitudeFade = 0.999f;
    private const float CutoffMargin = 0.2f;

    private const int SampleRateHz = 32000;
    private const int SampleRatePerMillisecond = SampleRateHz / 1000;
    private const int SincFilterQuality = 100;
    private const int SincOversamplingFactor = 32;
    private const int SincFilterWidth = SincFilterQuality * SincOversamplingFactor;
    private const int WaveformSize = SincFilterQuality + SampleRatePerMillisecond;

    private const float PositiveAmplitude = short.MaxValue * PwmScalar;
    private const float NegativeAmplitude = -PositiveAmplitude;
    private const float MsPerPitTick = 1000.0f / PitTimer.PitTickRate;
    private static readonly int MinimumCounter = Math.Max(1, 2 * PitTimer.PitTickRate / SampleRateHz);
    private IPitControl? _pitControl;
    private readonly EmulationLoopScheduler _scheduler;
    private readonly IEmulatedClock _clock;
    private readonly float[] _impulseLookup = new float[SincFilterWidth];
    private readonly ILoggerService _logger;
    private readonly Mixer _mixer;
    private readonly RWQueue<float> _outputQueue;
    private readonly PitChannelState _pitChannelState = new();
    private readonly MixerChannel _mixerChannel;
    private readonly EventHandler _tickHandler;
    private readonly float[] _waveform = new float[WaveformSize];
    private float _accumulator;
    private bool _disposed;

    private float _frameCounter;
    private double _lastTickTimeMs;
    private PpiPortB _portB;
    private PpiPortB _previousPortB;
    private int _tallyOfSilence;
    private int _waveformHead;

    /// <inheritdoc />
    public RWQueue<float> OutputQueue => _outputQueue;

    /// <inheritdoc />
    public MixerChannel Channel => _mixerChannel;

    /// <inheritdoc />
    public void NotifyLockMixer() {
        _outputQueue.Stop();
    }

    /// <inheritdoc />
    public void NotifyUnlockMixer() {
        _outputQueue.Start();
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="PcSpeaker" /> class.
    /// </summary>
    /// <param name="mixer">The shared software mixer used to render output.</param>
    /// <param name="state">Machine state for registering I/O handlers.</param>
    /// <param name="ioPortDispatcher">Dispatcher used to register the speaker port.</param>
    /// <param name="loggerService">Logger used for diagnostics.</param>
    /// <param name="scheduler">The event scheduler.</param>
    /// <param name="clock">The emulated clock for tick timing.</param>
    /// <param name="failOnUnhandledPort">Indicates whether unhandled port accesses should throw.</param>
    public PcSpeaker(
        Mixer mixer,
        State state,
        IOPortDispatcher ioPortDispatcher,
        ILoggerService loggerService,
        EmulationLoopScheduler scheduler,
        IEmulatedClock clock,
        bool failOnUnhandledPort)
        : base(state, failOnUnhandledPort, loggerService) {
        _logger = loggerService;
        _scheduler = scheduler;
        _clock = clock;
        _mixer = mixer;

        // Create queue first with initial capacity. Will be resized in callback.
        const int initialQueueSize = 256;
        _outputQueue = new RWQueue<float>(initialQueueSize);

        // Register after queue exists so NotifyLockMixer won't hit a null queue
        mixer.RegisterQueueNotifier(this);
        mixer.LockMixerThread();

        HashSet<ChannelFeature> features = new HashSet<ChannelFeature> {
            ChannelFeature.Sleep,
            ChannelFeature.ChorusSend,
            ChannelFeature.ReverbSend,
            ChannelFeature.Synthesizer,
        };
        // Pass 'this' to callback like DOSBox's std::bind(callback, _1, this) pattern
        _mixerChannel = mixer.AddChannel(
            framesRequested => _mixer.PullFromQueueCallback<PcSpeaker, float>(framesRequested, this),
            SampleRateHz, nameof(PcSpeaker), features);
        _mixerChannel.        AppVolume = new AudioFrame(1.0f, 1.0f);
        _mixerChannel.SetChannelMap(new StereoLine { Left = LineIndex.Left, Right = LineIndex.Left });
        _mixerChannel.SetPeakAmplitude((int)PositiveAmplitude);

        // Setup filters to emulate the bandwidth limited sound of the small PC speaker.
        // This more accurately reflects people's actual experience of the PC speaker
        // sound than the raw unfiltered output, and it's a lot more pleasant to listen to.
        const int highPassOrder = 3;
        const int highPassCutoffHz = 120;
        _mixerChannel.ConfigureHighPassFilter(highPassOrder, highPassCutoffHz);
        _mixerChannel.HighPassFilter = FilterState.On;

        const int lowPassOrder = 3;
        const int lowPassCutoffHz = 4300;
        _mixerChannel.ConfigureLowPassFilter(lowPassOrder, lowPassCutoffHz);
        _mixerChannel.LowPassFilter = FilterState.On;

        InitializeImpulseLUT();
        InitializePitChannelState();
        ioPortDispatcher.AddIOPortHandler(PcSpeakerPortNumber, this);
        _tickHandler = (_) => OnSchedulerTick();
        // Initialize last tick time before first tick fires
        _lastTickTimeMs = _clock.ElapsedTimeMs;
        _scheduler.AddEvent(_tickHandler, 1);
        mixer.UnlockMixerThread();
    }

    /// <inheritdoc />
    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Configures channel 2 of the PIT for the PC speaker using the supplied counter value and mode.
    /// </summary>
    /// <param name="count">The PIT reload value.</param>
    /// <param name="mode">The PIT operating mode that should be used.</param>
    public void SetCounter(int count, PitMode mode) {
        _logger.Debug("PCSPEAKER: Configuring counter with value {Count} in mode {Mode}", count, mode);

        _mixerChannel.WakeUp();

        float newIndex = GetPicTickIndex();
        float durationMs = MsPerPitTick * count;

        ForwardPit(newIndex);

        switch (mode) {
            case PitMode.InterruptOnTerminalCount:
                _pitChannelState.Index = 0.0f;
                _pitChannelState.Amplitude = NegativeAmplitude;
                _pitChannelState.MaxMilliseconds = durationMs;
                AddPitOutput(newIndex);
                break;

            case PitMode.OneShot:
                _pitChannelState.Mode1PendingMax = durationMs;
                if (_pitChannelState.Mode1WaitingForCounter) {
                    _pitChannelState.Mode1WaitingForCounter = false;
                    _pitChannelState.Mode1WaitingForTrigger = true;
                }

                break;

            case PitMode.RateGenerator:
            case PitMode.RateGeneratorAlias:
                _pitChannelState.Index = 0.0f;
                _pitChannelState.Amplitude = NegativeAmplitude;
                AddPitOutput(newIndex);
                _pitChannelState.MaxMilliseconds = durationMs;
                _pitChannelState.HalfMilliseconds = MsPerPitTick;
                break;

            case PitMode.SquareWave:
            case PitMode.SquareWaveAlias:
                if (count < MinimumCounter) {
                    _logger.Debug(
                        "PCSPEAKER: Counter value {Count} below minimum {Minimum}; forcing speaker inactive.",
                        count, MinimumCounter);
                    _pitChannelState.Amplitude = PositiveAmplitude;
                    _pitChannelState.Mode = PitMode.Inactive;
                    AddPitOutput(newIndex);
                    return;
                }

                _pitChannelState.NewMaxMilliseconds = durationMs;
                _pitChannelState.NewHalfMilliseconds = _pitChannelState.NewMaxMilliseconds / 2.0f;
                _logger.Debug(
                    "PCSPEAKER: Square wave period set to {Period} ms with half-period {HalfPeriod} ms.",
                    _pitChannelState.NewMaxMilliseconds, _pitChannelState.NewHalfMilliseconds);
                if (!_pitChannelState.Mode3Counting) {
                    _pitChannelState.Index = 0.0f;
                    _pitChannelState.MaxMilliseconds = _pitChannelState.NewMaxMilliseconds;
                    _pitChannelState.HalfMilliseconds = _pitChannelState.NewHalfMilliseconds;
                    if (_previousPortB.Timer2Gating) {
                        _pitChannelState.Mode3Counting = true;
                        _pitChannelState.Amplitude = PositiveAmplitude;
                        AddPitOutput(newIndex);
                    }
                }

                break;

            case PitMode.SoftwareStrobe:
                _pitChannelState.Amplitude = PositiveAmplitude;
                AddPitOutput(newIndex);
                _pitChannelState.Index = 0.0f;
                _pitChannelState.MaxMilliseconds = durationMs;
                break;

            default:
                _logger.Warning("PCSPEAKER: Unhandled speaker PIT mode {Mode} with count {Count}", mode, count);
                return;
        }

        _pitChannelState.Mode = mode;
    }

    /// <summary>
    ///     Updates the speaker control state based on PIT mode transitions.
    /// </summary>
    /// <param name="mode">The PIT mode that should now be active for the speaker.</param>
    public void SetPitControl(PitMode mode) {
        _logger.Debug("PCSPEAKER: Updating PIT control for mode {Mode}", mode);

        _mixerChannel.WakeUp();

        float newIndex = GetPicTickIndex();
        ForwardPit(newIndex);

        switch (mode) {
            case PitMode.OneShot:
                _pitChannelState.Mode = mode;
                _pitChannelState.Amplitude = PositiveAmplitude;
                _pitChannelState.Mode1WaitingForCounter = true;
                _pitChannelState.Mode1WaitingForTrigger = false;
                AddPitOutput(newIndex);
                break;

            case PitMode.SquareWave:
            case PitMode.SquareWaveAlias:
                _pitChannelState.Mode = mode;
                _pitChannelState.Amplitude = PositiveAmplitude;
                _pitChannelState.Mode3Counting = false;
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

        _portB.Timer2GatingAlias = _pitControl?.IsChannel2OutputHigh == true;

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
            _scheduler.RemoveEvents(_tickHandler);
        }
        _disposed = true;
    }

    private void OnSchedulerTick() {
        // Record tick start time for GetPicTickIndex() calculations
        // Reference: DOSBox's PIC_TickIndex() returns position within current tick
        _lastTickTimeMs = _clock.ElapsedTimeMs;

        if (!_mixerChannel.IsEnabled) {
            _scheduler.AddEvent(_tickHandler, 1);
            return;
        }
        _frameCounter += _mixerChannel.FramesPerTick;
        int requestedFrames = (int)Math.Floor(_frameCounter);
        _frameCounter -= requestedFrames;

        if (requestedFrames > 0) {
            if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                _logger.Verbose("PCSPEAKER: Tick callback requestedFrames={Frames}", requestedFrames);
            }
            PicCallback(requestedFrames);
        }

        // Reschedule for next tick (1ms) - matches DOSBox's TIMER_AddTickHandler behavior
        _scheduler.AddEvent(_tickHandler, 1);
    }

    private void PicCallback(int requestedFrames) {
        ForwardPit(1.0f);
        _pitChannelState.LastIndex = 0.0f;

        int remainingFrames = requestedFrames;

        while (remainingFrames > 0) {
            // Pop the first sample off the waveform
            _accumulator += PopWaveformSample();

            EnqueueSample(_accumulator);
            remainingFrames--;

            // Keep a tally of sequential silence so we can sleep the channel
            _tallyOfSilence = Math.Abs(_accumulator) > 1.0f ? 0 : _tallyOfSilence + 1;

            // Scale down the running volume amplitude. Eventually it will
            // hit 0 if no other waveforms are generated.
            _accumulator *= SincAmplitudeFade;
        }

        // Write silence if the waveform deque ran out
        if (remainingFrames > 0) {
            _pitChannelState.PreviousAmplitude = NeutralAmplitude;
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
            switch (_pitChannelState.Mode) {
                case PitMode.OneShot:
                    if (_pitChannelState.Mode1WaitingForCounter) {
                        break;
                    }

                    _pitChannelState.Amplitude = NegativeAmplitude;
                    _pitChannelState.Index = 0.0f;
                    _pitChannelState.MaxMilliseconds = _pitChannelState.Mode1PendingMax;
                    _pitChannelState.Mode1WaitingForTrigger = false;
                    break;

                case PitMode.SquareWave:
                case PitMode.SquareWaveAlias:
                    _pitChannelState.Mode3Counting = true;
                    _pitChannelState.Index = 0.0f;
                    _pitChannelState.MaxMilliseconds = _pitChannelState.NewMaxMilliseconds;
                    _pitChannelState.NewHalfMilliseconds = _pitChannelState.NewMaxMilliseconds / 2.0f;
                    _pitChannelState.HalfMilliseconds = _pitChannelState.NewHalfMilliseconds;
                    _pitChannelState.Amplitude = PositiveAmplitude;
                    break;
            }
        } else if (!newPortB.Timer2Gating && _pitChannelState.Mode is PitMode.SquareWave or PitMode.SquareWaveAlias) {
            _pitChannelState.Amplitude = PositiveAmplitude;
            _pitChannelState.Mode3Counting = false;
        }

        AddImpulse(newIndex, newPortB.SpeakerOutput ? _pitChannelState.Amplitude : NegativeAmplitude);
    }

    private void AddPitOutput(float index) {
        if (_previousPortB.SpeakerOutput) {
            AddImpulse(index, _pitChannelState.Amplitude);
        }
    }

    private void ForwardPit(float newIndex) {
        float passed = newIndex - _pitChannelState.LastIndex;
        float delayBase = _pitChannelState.LastIndex;
        _pitChannelState.LastIndex = newIndex;

        switch (_pitChannelState.Mode) {
            case PitMode.Inactive:
                return;

            case PitMode.InterruptOnTerminalCount:
                if (_pitChannelState.Index >= _pitChannelState.MaxMilliseconds) {
                    return;
                }

                _pitChannelState.Index += passed;
                if (_pitChannelState.Index >= _pitChannelState.MaxMilliseconds) {
                    float delay = delayBase + _pitChannelState.MaxMilliseconds - _pitChannelState.Index + passed;
                    _pitChannelState.Amplitude = PositiveAmplitude;
                    AddPitOutput(delay);
                }

                return;

            case PitMode.OneShot:
                if (_pitChannelState.Mode1WaitingForCounter || _pitChannelState.Mode1WaitingForTrigger) {
                    return;
                }

                if (_pitChannelState.Index >= _pitChannelState.MaxMilliseconds) {
                    return;
                }

                _pitChannelState.Index += passed;
                if (_pitChannelState.Index >= _pitChannelState.MaxMilliseconds) {
                    float delay = delayBase + _pitChannelState.MaxMilliseconds - _pitChannelState.Index + passed;
                    _pitChannelState.Amplitude = PositiveAmplitude;
                    AddPitOutput(delay);
                    _pitChannelState.Mode1WaitingForTrigger = true;
                }

                return;

            case PitMode.RateGenerator:
            case PitMode.RateGeneratorAlias:
                while (passed > 0.0f) {
                    if (_pitChannelState.Index >= _pitChannelState.HalfMilliseconds) {
                        if (_pitChannelState.Index + passed >= _pitChannelState.MaxMilliseconds) {
                            float delay = _pitChannelState.MaxMilliseconds - _pitChannelState.Index;
                            delayBase += delay;
                            passed -= delay;
                            _pitChannelState.Amplitude = NegativeAmplitude;
                            AddPitOutput(delayBase);
                            _pitChannelState.Index = 0.0f;
                        } else {
                            _pitChannelState.Index += passed;
                            return;
                        }
                    } else {
                        if (_pitChannelState.Index + passed >= _pitChannelState.HalfMilliseconds) {
                            float delay = _pitChannelState.HalfMilliseconds - _pitChannelState.Index;
                            delayBase += delay;
                            passed -= delay;
                            _pitChannelState.Amplitude = PositiveAmplitude;
                            AddPitOutput(delayBase);
                            _pitChannelState.Index = _pitChannelState.HalfMilliseconds;
                        } else {
                            _pitChannelState.Index += passed;
                            return;
                        }
                    }
                }

                break;

            case PitMode.SquareWave:
            case PitMode.SquareWaveAlias:
                if (!_pitChannelState.Mode3Counting) {
                    break;
                }

                while (passed > 0.0f) {
                    if (_pitChannelState.Index >= _pitChannelState.HalfMilliseconds) {
                        if (_pitChannelState.Index + passed >= _pitChannelState.MaxMilliseconds) {
                            float delay = _pitChannelState.MaxMilliseconds - _pitChannelState.Index;
                            delayBase += delay;
                            passed -= delay;
                            _pitChannelState.Amplitude = PositiveAmplitude;
                            AddPitOutput(delayBase);
                            _pitChannelState.Index = 0.0f;
                            _pitChannelState.MaxMilliseconds = _pitChannelState.NewMaxMilliseconds;
                            _pitChannelState.HalfMilliseconds = _pitChannelState.NewHalfMilliseconds;
                        } else {
                            _pitChannelState.Index += passed;
                            return;
                        }
                    } else {
                        if (_pitChannelState.Index + passed >= _pitChannelState.HalfMilliseconds) {
                            float delay = _pitChannelState.HalfMilliseconds - _pitChannelState.Index;
                            delayBase += delay;
                            passed -= delay;
                            _pitChannelState.Amplitude = NegativeAmplitude;
                            AddPitOutput(delayBase);
                            _pitChannelState.Index = _pitChannelState.HalfMilliseconds;
                            _pitChannelState.MaxMilliseconds = _pitChannelState.NewMaxMilliseconds;
                            _pitChannelState.HalfMilliseconds = _pitChannelState.NewHalfMilliseconds;
                        } else {
                            _pitChannelState.Index += passed;
                            return;
                        }
                    }
                }

                break;

            case PitMode.SoftwareStrobe:
                if (_pitChannelState.Index < _pitChannelState.MaxMilliseconds && _pitChannelState.Index + passed >= _pitChannelState.MaxMilliseconds) {
                    float delay = _pitChannelState.MaxMilliseconds - _pitChannelState.Index;
                    delayBase += delay;
                    _pitChannelState.Amplitude = NegativeAmplitude;
                    AddPitOutput(delayBase);
                    _pitChannelState.Index = _pitChannelState.MaxMilliseconds;
                } else {
                    _pitChannelState.Index += passed;
                }

                break;
        }
    }

    private void AddImpulse(float index, float amplitude) {
        if (_mixerChannel.WakeUp()) {
            _pitChannelState.PreviousAmplitude = NeutralAmplitude;
        }

        if (Math.Abs(amplitude - _pitChannelState.PreviousAmplitude) < 1e-6f) {
            return;
        }

        _pitChannelState.PreviousAmplitude = amplitude;

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
        _outputQueue.NonblockingEnqueue(value);
    }

    /// <summary>
    ///     Returns the current position within the tick (0.0 to 1.0).
    /// </summary>
    private float GetPicTickIndex() {
        double currentTime = _clock.ElapsedTimeMs;
        double elapsed = currentTime - _lastTickTimeMs;
        // Clamp to 0.0 - 1.0 (position within current 1ms tick)
        return (float)Math.Clamp(elapsed, 0.0, 1.0);
    }

    private void InitializeImpulseLUT() {
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

    private void InitializePitChannelState() {
        _pitChannelState.MaxMilliseconds = 1320000.0f / PitTimer.PitTickRate;
        _pitChannelState.NewMaxMilliseconds = _pitChannelState.MaxMilliseconds;
        _pitChannelState.HalfMilliseconds = _pitChannelState.MaxMilliseconds / 2.0f;
        _pitChannelState.NewHalfMilliseconds = _pitChannelState.HalfMilliseconds;
        _pitChannelState.Mode = PitMode.SquareWave;
        _pitChannelState.Amplitude = PositiveAmplitude;
        _pitChannelState.PreviousAmplitude = NegativeAmplitude;
        _pitChannelState.Mode1WaitingForTrigger = true;
    }

    /// <summary>
    ///     Tracks the internal state of the PIT channel driving the PC speaker.
    /// </summary>
    [DebuggerDisplay("Mode={Mode}, Amplitude={Amplitude}, IndexInWaveForm={Index}/{MaxMilliseconds}ms, Mode3Counting={Mode3Counting}, Mode1WaitingForTrigger={Mode1WaitingForTrigger}")]
    private sealed class PitChannelState {
        /// <summary>
        ///     Current output amplitude of the PIT channel.
        /// </summary>
        public float Amplitude { get; set; }

        /// <summary>
        ///     Half-period duration in milliseconds, used as the toggle point in square and rate-generator modes.
        /// </summary>
        public float HalfMilliseconds { get; set; }

        /// <summary>
        ///     Current position within the waveform period, in milliseconds.
        /// </summary>
        public float Index { get; set; }

        /// <summary>
        ///     Last processed tick index, used to compute elapsed time between updates.
        /// </summary>
        public float LastIndex { get; set; }

        /// <summary>
        ///     Full period duration in milliseconds derived from the PIT reload value.
        /// </summary>
        public float MaxMilliseconds { get; set; }

        /// <summary>
        ///     Active PIT operating mode for the speaker channel.
        /// </summary>
        public PitMode Mode { get; set; }

        /// <summary>
        ///     Pending maximum duration for one-shot mode, applied when the gate is triggered.
        /// </summary>
        public float Mode1PendingMax { get; set; }

        /// <summary>
        ///     Indicates one-shot mode is waiting for a counter value to be loaded.
        /// </summary>
        public bool Mode1WaitingForCounter { get; set; }

        /// <summary>
        ///     Indicates one-shot mode is waiting for a rising-edge gate trigger.
        /// </summary>
        public bool Mode1WaitingForTrigger { get; set; } = true;

        /// <summary>
        ///     Indicates square-wave mode is actively counting (gate is high).
        /// </summary>
        public bool Mode3Counting { get; set; }

        /// <summary>
        ///     Latched half-period applied at the start of the next full cycle.
        /// </summary>
        public float NewHalfMilliseconds { get; set; }

        /// <summary>
        ///     Latched full period applied at the start of the next full cycle.
        /// </summary>
        public float NewMaxMilliseconds { get; set; }

        /// <summary>
        ///     Previous amplitude value, used to detect transitions for impulse generation.
        /// </summary>
        public float PreviousAmplitude { get; set; }
    }
}
