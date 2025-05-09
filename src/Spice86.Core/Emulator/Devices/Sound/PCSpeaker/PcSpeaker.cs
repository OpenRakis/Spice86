namespace Spice86.Core.Emulator.Devices.Sound.PCSpeaker;

using Spice86.Core.Backend.Audio.IirFilters;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Diagnostics;

/// <summary>
/// Emulates a PC Speaker with both discrete sampling and impulse response capabilities.
/// </summary>
public sealed class PcSpeaker : DefaultIOPortHandler, IDisposable {
    private const int PcSpeakerPortNumber = 0x61;

    // Amplitude constants
    private const float AmplitudePositive = 32767.0f * 0.75f; // Match discrete model scalar

    private const float AmplitudeNegative = -AmplitudePositive;
    private const float AmplitudeNeutral = 0.0f;

    // Filter settings
    private const int HighPassFilterOrder = 3;

    private const int HighPassCutoffFreqHz = 120;
    private const int LowPassFilterOrder = 2; // Use order 2 like in discrete model
    private const int LowPassCutoffFreqHz = 4800; // Match discrete model cutoff

    private readonly LowPass _lowPassFilter = new();
    private readonly HighPass _highPassFilter = new();
    private readonly SoundChannel _soundChannel;
    private readonly int _outputSampleRate = 32000; // Match DOSBox sample rate
    private readonly int _ticksPerSample;
    private readonly Pit8254Counter _pit8254Counter;
    private readonly DeviceThread _deviceThread;
    private readonly byte[] _monoBuffer;

    // Delay entries queue for audio rendering (like in discrete model)
    private readonly Queue<DelayEntry> _delayQueue = new();

    // Port and PIT state tracking
    private SpeakerControl _controlRegister = SpeakerControl.UseTimer;

    private SpeakerControl _prevControlRegister = SpeakerControl.UseTimer;
    private PitState _pitState = new();
    private bool _pitGateEnabled;
    private bool _prevPitGateEnabled;

    private float _volCurrent = 0.0f;
    private float _volWant = 0.0f;
    private float _lastIndex = 0.0f;
    private int _tallySilence = 0;
    private bool _disposed;

    /// <summary>
    /// Stores PIT state for tracking between mode changes.
    /// </summary>
    private class PitState {
        public int PreviousMode = 3; // SquareWave
        public int CurrentMode = 3; // SquareWave
        public float Index = 0.0f;
        public float LastPit = 0.0f;
        public float MaxMs = 1320000.0f / Pit8254Counter.HardwareFrequency;
        public float HalfMs = 1320000.0f / (2 * Pit8254Counter.HardwareFrequency);
        public float NewMaxMs = 1320000.0f / Pit8254Counter.HardwareFrequency;
        public float NewHalfMs = 1320000.0f / (2 * Pit8254Counter.HardwareFrequency);
        public bool Mode3Counting = false;
        public bool Mode1WaitingForCounter = false;
        public bool Mode1WaitingForTrigger = true;
        public float Mode1PendingMax = 0.0f;
    }

    /// <summary>
    /// Stores a delay entry for the PC speaker output.
    /// </summary>
    private struct DelayEntry {
        public float Index { get; }
        public float Volume { get; }

        public DelayEntry(float index, float volume) {
            Index = index;
            Volume = volume;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PcSpeaker"/> class.
    /// </summary>
    public PcSpeaker(SoftwareMixer softwareMixer,
        State state,
        Pit8254Counter pit8254Counter,
        IOPortDispatcher ioPortDispatcher,
        IPauseHandler pauseHandler,
        ILoggerService loggerService,
        bool failOnUnhandledPort) : base(state, failOnUnhandledPort, loggerService) {
        _soundChannel = softwareMixer.CreateChannel(nameof(PcSpeaker));
        _pit8254Counter = pit8254Counter;
        pit8254Counter.SettingChangedEvent += (_, _) => OnTimerSettingChanged();
        _ticksPerSample = (int)(Stopwatch.Frequency / (double)_outputSampleRate);
        InitPortHandlers(ioPortDispatcher);

        _deviceThread = new DeviceThread(nameof(PcSpeaker), PlaybackLoopBody, pauseHandler, loggerService);
        _monoBuffer = new byte[4096];

        // Setup the low-pass and high-pass filters
        _lowPassFilter.Setup(_outputSampleRate, LowPassCutoffFreqHz);
        _highPassFilter.Setup(_outputSampleRate, HighPassCutoffFreqHz);
    }

    // Calculate minimum tick rate directly in the property
    private int MinimumTickRate => (int)((Pit8254Counter.HardwareFrequency + _outputSampleRate / 2 - 1) / (_outputSampleRate / 2));

    /// <summary>
    /// Fills a buffer with silence.
    /// </summary>
    private static void GenerateSilence(Span<byte> buffer) => buffer.Fill(127);

    /// <summary>
    /// Called when PC speaker has been disabled.
    /// </summary>
    private void SpeakerDisabled() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("PC Speaker disabled");
        }

        AddDelayEntry(0.0f, NeutralOr(AmplitudeNegative));
        _deviceThread.Pause();
    }

    /// <summary>
    /// Called when timer settings change (from PIT).
    /// </summary>
    private void OnTimerSettingChanged() {
        float newIndex = 0.0f;
        ForwardPIT(newIndex);

        _pitState.PreviousMode = _pitState.CurrentMode;
        _pitState.CurrentMode = _pit8254Counter.Mode;

        int counter = _pit8254Counter.ReloadValue;
        float counterMs = 1000.0f * counter / Pit8254Counter.HardwareFrequency;

        // Handle counter values based on PIT mode (like in SetCounter)
        switch (_pitState.CurrentMode) {
            case 0: // InterruptOnTerminalCount (RealSound PWM)
                if (!_pitGateEnabled) {
                    return;
                }

                _pitState.Index = 0;
                _pitState.LastPit = AmplitudeNegative;
                _pitState.MaxMs = counterMs;

                AddDelayEntry(newIndex, _pitState.LastPit);
                break;

            case 1: // OneShot
                if (!_pitGateEnabled) {
                    return;
                }

                _pitState.Mode1PendingMax = counterMs;
                if (_pitState.Mode1WaitingForCounter) {
                    _pitState.Mode1WaitingForCounter = false;
                    _pitState.Mode1WaitingForTrigger = true;
                }

                _pitState.LastPit = AmplitudePositive;
                AddDelayEntry(newIndex, _pitState.LastPit);
                break;

            case 2: // RateGenerator
                _pitState.Index = 0;
                _pitState.LastPit = AmplitudeNegative;
                AddDelayEntry(newIndex, _pitState.LastPit);

                _pitState.HalfMs = 1000.0f / Pit8254Counter.HardwareFrequency; // 1 PIT tick
                _pitState.MaxMs = counterMs;
                break;

            case 3: // SquareWave (most commonly used)
                if (counter == 0 || counter < MinimumTickRate) {
                    // Skip frequencies that can't be represented
                    _pitState.LastPit = 0;
                    _pitState.CurrentMode = 0; // Fallback to mode 0
                    return;
                }

                _pitState.NewMaxMs = counterMs;
                _pitState.NewHalfMs = _pitState.NewMaxMs / 2;

                if (!_pitState.Mode3Counting) {
                    _pitState.Index = 0;
                    _pitState.MaxMs = _pitState.NewMaxMs;
                    _pitState.HalfMs = _pitState.NewHalfMs;

                    if (_pitGateEnabled) {
                        _pitState.Mode3Counting = true;
                        _pitState.LastPit = AmplitudePositive;
                        AddDelayEntry(newIndex, _pitState.LastPit);
                    }
                }
                break;

            case 4: // SoftwareStrobe
                _pitState.LastPit = AmplitudePositive;
                AddDelayEntry(newIndex, _pitState.LastPit);
                _pitState.Index = 0;
                _pitState.MaxMs = counterMs;
                break;

            default:
                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                    _loggerService.Warning("PCSPEAKER: Unhandled speaker PIT mode: {Mode}", _pit8254Counter.Mode);
                }
                break;
        }

        _deviceThread.Resume();
    }

    /// <summary>
    /// Determines if the current waveform is a square wave by examining PIT state.
    /// </summary>
    private bool IsWaveSquare() {
        // Similar logic to IsWaveSquare in pcspeaker_discrete.cpp
        const int wasToggled = 0b10 + 0b11;
        const int wasSteadilyOn = 0b11;
        const int wasToggledOn = 0b01;

        int temporalPitState = _pitState.PreviousMode + _pitState.CurrentMode;
        int temporalPwmState = (_prevPitGateEnabled && _prevControlRegister.HasFlag(SpeakerControl.SpeakerOn) ? 1 : 0) +
                              (_pitGateEnabled && _controlRegister.HasFlag(SpeakerControl.SpeakerOn) ? 1 : 0);

        // We have a sine-wave if the PIT was steadily off and ...
        if (temporalPitState == 0)
            // The PWM toggled an ongoing PIT state or turned on PIT-mode from an off-state
            if (temporalPwmState == wasToggled || temporalPwmState == wasSteadilyOn)
                return false;

        // We have a sine-wave if the PIT was steadily on and ...
        if (temporalPitState == wasSteadilyOn)
            // the PWM was turned on from an off-state
            if (temporalPwmState == wasToggledOn)
                return false;

        return true;
    }

    /// <summary>
    /// Adds a delay entry to the queue.
    /// </summary>
    private void AddDelayEntry(float index, float vol) {
        // Apply square wave scalar for square waves - like in discrete model
        if (IsWaveSquare()) {
            vol *= 0.5f; // Similar to sqw_scalar in discrete model
        }

        _delayQueue.Enqueue(new DelayEntry(index, vol));

        // Wake the sound channel like in the C++ code
        _deviceThread.Resume();
    }

    /// <summary>
    /// Returns neutral amplitude if channel is disabled, otherwise returns fallback.
    /// </summary>
    private float NeutralOr(float fallback) {
        return _tallySilence > 100 ? AmplitudeNeutral : fallback;
    }

    /// <summary>
    /// Returns, in order of preference:
    /// - Neutral voltage, if the speaker is fully faded
    /// - The last active PIT voltage to stitch on-going playback
    /// - The fallback voltage to kick start a new sound pattern
    /// </summary>
    private float NeutralLastPitOr(float fallback) {
        bool useLast = Math.Abs(_pitState.LastPit) > Math.Abs(AmplitudeNeutral);
        return NeutralOr(useLast ? _pitState.LastPit : fallback);
    }

    /// <summary>
    /// Advances PIT state over time.
    /// </summary>
    private void ForwardPIT(float newIndex) {
        float passed = newIndex - _lastIndex;
        float delayBase = _lastIndex;
        _lastIndex = newIndex;

        switch (_pitState.CurrentMode) {
            case 0: // InterruptOnTerminalCount
                return;

            case 1: // OneShot
                return;

            case 2: // RateGenerator
                while (passed > 0) {
                    // Passed the initial low cycle?
                    if (_pitState.Index >= _pitState.HalfMs) {
                        // Start a new low cycle
                        if ((_pitState.Index + passed) >= _pitState.MaxMs) {
                            float delay = _pitState.MaxMs - _pitState.Index;
                            delayBase += delay;
                            passed -= delay;
                            _pitState.LastPit = AmplitudeNegative;
                            if (_pitGateEnabled && _controlRegister.HasFlag(SpeakerControl.SpeakerOn)) {
                                AddDelayEntry(delayBase, _pitState.LastPit);
                            }
                            _pitState.Index = 0;
                        } else {
                            _pitState.Index += passed;
                            return;
                        }
                    } else {
                        if ((_pitState.Index + passed) >= _pitState.HalfMs) {
                            float delay = _pitState.HalfMs - _pitState.Index;
                            delayBase += delay;
                            passed -= delay;
                            _pitState.LastPit = AmplitudePositive;
                            if (_pitGateEnabled && _controlRegister.HasFlag(SpeakerControl.SpeakerOn)) {
                                AddDelayEntry(delayBase, _pitState.LastPit);
                            }
                            _pitState.Index = _pitState.HalfMs;
                        } else {
                            _pitState.Index += passed;
                            return;
                        }
                    }
                }
                break;

            case 3: // SquareWave
                if (!_pitState.Mode3Counting) {
                    break;
                }

                while (passed > 0) {
                    // Determine where in the wave we're located
                    if (_pitState.Index >= _pitState.HalfMs) {
                        if ((_pitState.Index + passed) >= _pitState.MaxMs) {
                            float delay = _pitState.MaxMs - _pitState.Index;
                            delayBase += delay;
                            passed -= delay;
                            _pitState.LastPit = AmplitudePositive;
                            if (_pitGateEnabled && _controlRegister.HasFlag(SpeakerControl.SpeakerOn)) {
                                AddDelayEntry(delayBase, _pitState.LastPit);
                            }
                            _pitState.Index = 0;
                            // Load the new count
                            _pitState.HalfMs = _pitState.NewHalfMs;
                            _pitState.MaxMs = _pitState.NewMaxMs;
                        } else {
                            _pitState.Index += passed;
                            return;
                        }
                    } else {
                        if ((_pitState.Index + passed) >= _pitState.HalfMs) {
                            float delay = _pitState.HalfMs - _pitState.Index;
                            delayBase += delay;
                            passed -= delay;
                            _pitState.LastPit = AmplitudeNegative;
                            if (_pitGateEnabled && _controlRegister.HasFlag(SpeakerControl.SpeakerOn)) {
                                AddDelayEntry(delayBase, _pitState.LastPit);
                            }
                            _pitState.Index = _pitState.HalfMs;
                            // Load the new count
                            _pitState.HalfMs = _pitState.NewHalfMs;
                            _pitState.MaxMs = _pitState.NewMaxMs;
                        } else {
                            _pitState.Index += passed;
                            return;
                        }
                    }
                }
                break;

            case 4: // SoftwareStrobe
                if (_pitState.Index < _pitState.MaxMs) {
                    // Check if we're going to pass the end this block
                    if (_pitState.Index + passed >= _pitState.MaxMs) {
                        float delay = _pitState.MaxMs - _pitState.Index;
                        delayBase += delay;
                        _pitState.LastPit = AmplitudeNegative;
                        if (_pitGateEnabled && _controlRegister.HasFlag(SpeakerControl.SpeakerOn)) {
                            AddDelayEntry(delayBase, _pitState.LastPit);
                        }
                        _pitState.Index = _pitState.MaxMs;
                    } else {
                        _pitState.Index += passed;
                    }
                }
                break;

            default:
                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                    _loggerService.Warning("PCSPEAKER: Unhandled PIT mode: {Mode}", _pitState.CurrentMode);
                }
                break;
        }
    }

    /// <summary>
    /// Handles a PIT trigger event (rising edge on gate input).
    /// </summary>
    private void HandlePitTrigger() {
        switch (_pitState.CurrentMode) {
            case 1: // OneShot
                if (_pitState.Mode1WaitingForCounter) {
                    break;
                }
                _pitState.LastPit = AmplitudeNegative;
                _pitState.Index = 0;
                _pitState.MaxMs = _pitState.Mode1PendingMax;
                _pitState.Mode1WaitingForTrigger = false;
                break;

            case 3: // SquareWave
                _pitState.Mode3Counting = true;
                _pitState.Index = 0;
                _pitState.MaxMs = _pitState.NewMaxMs;
                _pitState.HalfMs = _pitState.NewHalfMs;
                _pitState.LastPit = AmplitudePositive;
                break;

            default:
                // Other modes don't need special handling for triggers
                break;
        }
    }

    /// <summary>
    /// Main audio playback loop.
    /// </summary>
    private void PlaybackLoopBody() {
        ForwardPIT(1.0f);
        _lastIndex = 0.0f;
        var sampleBase = 0.0f;

        // Add epsilon to ensure entries at the end of cycle get processed
        const float periodPerFrameMs = float.Epsilon + 1.0f / 200.0f; // Similar to period_per_frame_ms

        // Fill buffer with samples
        int samplesGenerated = 0;
        bool hasSoundOutput = false;

        for (int i = 0; i < _monoBuffer.Length && i < 200; i++) {
            var index = sampleBase;
            sampleBase += periodPerFrameMs;
            var end = sampleBase;

            float value = 0.0f;

            while (index < end) {
                // Check if there is an upcoming event
                bool hasEntries = _delayQueue.Count > 0;
                float firstIndex = hasEntries ? _delayQueue.Peek().Index : 0.0f;

                if (hasEntries && firstIndex <= index) {
                    _volWant = _delayQueue.Dequeue().Volume;
                    continue;
                }

                float volEnd = (hasEntries && firstIndex < end) ? firstIndex : end;
                float volLen = volEnd - index;

                // Check if we have to slide the volume (smooth transitions)
                float volDiff = _volWant - _volCurrent;

                if (Math.Abs(volDiff) < 0.01f) {
                    // Volume is close enough, just use target
                    value += _volWant * volLen;
                    _volCurrent = _volWant;
                    index += volLen;
                } else {
                    // Gradually adjust volume - similar to discrete implementation
                    float speakerSpeed = AmplitudePositive * 2.0f / 0.070f;
                    float volTime = Math.Abs(volDiff) / speakerSpeed;

                    if (volTime <= volLen) {
                        // Volume reaches endpoint in this block
                        value += volTime * _volCurrent;
                        value += volTime * volDiff / 2;
                        index += volTime;
                        _volCurrent = _volWant;
                    } else {
                        // Volume still increasing/decreasing
                        value += _volCurrent * volLen;

                        float volCurDelta = speakerSpeed * volLen;
                        _volCurrent += Math.Sign(volDiff) * volCurDelta;

                        float valueDelta = volCurDelta * volLen / 2.0f;
                        value += Math.Sign(volDiff) * valueDelta;
                        index += volLen;
                    }
                }
            }

            // Normalize and convert to byte
            float sampleValue = value / periodPerFrameMs;

            // Apply filters similar to C++ implementation
            sampleValue = (float)_highPassFilter.Filter(sampleValue);
            sampleValue = (float)_lowPassFilter.Filter(sampleValue);

            // Scale to 0-255 range and store in buffer
            _monoBuffer[i] = (byte)Math.Clamp((sampleValue / AmplitudePositive * 64) + 127, 0, 255);
            samplesGenerated++;

            if (Math.Abs(sampleValue) > 1.0f) {
                hasSoundOutput = true;
            }
        }

        // Update silence tally
        if (hasSoundOutput) {
            _tallySilence = 0;
        } else {
            _tallySilence++;
            if (_tallySilence > 100) {
                _deviceThread.Pause();
            }
        }

        // Send buffer to audio system
        if (samplesGenerated > 0) {
            _soundChannel.Render(_monoBuffer.AsSpan(0, samplesGenerated));
        }
    }

    /// <summary>
    /// Handles reading from the PC speaker control port.
    /// </summary>
    public override byte ReadByte(ushort port) {
        if (port != PcSpeakerPortNumber) {
            return base.ReadByte(port);
        }

        byte value = (byte)_controlRegister;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("PC Speaker get value {PCSpeakerValue}", ConvertUtils.ToHex8(value));
        }

        return value;
    }

    /// <summary>
    /// Registers port handlers with the dispatcher.
    /// </summary>
    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(PcSpeakerPortNumber, this);
    }

    /// <summary>
    /// Handles writes to PC speaker control port (port 0x61).
    /// </summary>
    public override void WriteByte(ushort port, byte value) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("PC Speaker set value {PCSpeakerValue}", ConvertUtils.ToHex8(value));
        }

        if (port == PcSpeakerPortNumber) {
            _prevControlRegister = _controlRegister;
            SpeakerControl newValue = (SpeakerControl)value;

            bool wasEnabled = IsEnabled(_controlRegister);
            bool willBeEnabled = IsEnabled(newValue);

            // Handle PIT gate changes (similar to timer2_gating in C++ code)
            _prevPitGateEnabled = _pitGateEnabled;
            _pitGateEnabled = newValue.HasFlag(SpeakerControl.UseTimer);

            // Detect gate rising edge (important for mode triggering)
            bool pitTrigger = !_prevPitGateEnabled && _pitGateEnabled;

            if (pitTrigger) {
                // PIT clock gate enable rising edge is a trigger
                HandlePitTrigger();
            } else if (!_pitGateEnabled) {
                // Low gate forces PIT output high in some modes
                if (_pitState.CurrentMode == 3) { // SquareWave
                    _pitState.LastPit = AmplitudePositive;
                    _pitState.Mode3Counting = false;
                }
            }

            // Handle speaker output changes
            if (newValue.HasFlag(SpeakerControl.SpeakerOn)) {
                if (_pitGateEnabled) {
                    // Speaker is directly connected to PIT
                    AddDelayEntry(0, _pitState.LastPit);
                } else {
                    // Speaker is forced low when gate is disabled
                    AddDelayEntry(0, AmplitudeNegative);
                }
            } else if (_prevControlRegister.HasFlag(SpeakerControl.SpeakerOn)) {
                // Speaker turning off
                AddDelayEntry(0, AmplitudeNeutral);
            }

            // Start or stop the device thread as needed
            if (wasEnabled && !willBeEnabled) {
                SpeakerDisabled();
            } else if (!wasEnabled && willBeEnabled) {
                _deviceThread.StartThreadIfNeeded();
            }

            _controlRegister = newValue;
        } else {
            base.WriteByte(port, value);
        }
    }

    /// <summary>
    /// Determines if the speaker is enabled based on control register.
    /// </summary>
    private bool IsEnabled(SpeakerControl registerValue) {
        return (registerValue & SpeakerControl.SpeakerOn) != 0 && (registerValue & SpeakerControl.UseTimer) != 0;
    }

    /// <summary>
    /// Disposes resources used by this instance.
    /// </summary>
    public void Dispose() {
        Dispose(disposing: true);
    }

    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _deviceThread.Dispose();
            }
            _disposed = true;
        }
    }
}