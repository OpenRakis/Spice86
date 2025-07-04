namespace Spice86.Core.Emulator.Devices.Sound.PCSpeaker;

using Spice86.Core.Backend.Audio.IirFilters;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using System;

/// <summary>
/// Emulates the PC Speaker found in IBM compatible PCs.
/// </summary>
public sealed partial class PcSpeaker : DefaultIOPortHandler, IDisposable {
    // IO port constants
    private const int PcSpeakerPortNumber = 0x61;
    
    // Audio constants
    private const int SampleRate = 48000;
    private const int FramesPerBuffer = 512;
    private const float PitTickRate = 1193182.0f;
    private const float MsPerPitTick = 1000.0f / PitTickRate;
    
    // PC Speaker amplitude settings - carefully calibrated for DOSBox compatibility
    private const float PositiveAmplitude = 0.5f;
    private const float NegativeAmplitude = -0.5f;
    private const float NeutralAmplitude = 0.0f;
    
    private readonly Pit8254Counter _pit8254Counter;
    private readonly SoundChannel _soundChannel;
    private readonly DeviceThread _deviceThread;
    private bool _disposed;
    
    // Speaker state tracking
    private readonly PitState _pitState = new();
    private readonly PpiPortB _portB = new();
    private int _prevPitMode = 3; // Default is square wave (mode 3)
    
    private readonly float[] _audioBuffer;
    
    // Audio cycle tracking
    private float _cyclePosition = 0;
    private float _cycleStep = 0;
    
    private readonly LowPass _lowPassFilter = new();
    private readonly HighPass _highPassFilter = new();
    private const int HighPassCutoffHz = 100;  // Filter out DC and very low frequencies
    private const int LowPassCutoffHz = 8000;  // Filter out high frequencies
    private const double FilterQ = 0.7071;     // Standard Butterworth Q factor
    
    /// <summary>
    /// Initializes a new instance of the <see cref="PcSpeaker"/> class.
    /// </summary>
    public PcSpeaker(
        SoftwareMixer softwareMixer,
        State state,
        Pit8254Counter pit8254Counter,
        IOPortDispatcher ioPortDispatcher,
        IPauseHandler pauseHandler,
        ILoggerService loggerService,
        bool failOnUnhandledPort) : base(state, failOnUnhandledPort, loggerService) {
        
        _pit8254Counter = pit8254Counter;
        _audioBuffer = new float[FramesPerBuffer];
        _soundChannel = softwareMixer.CreateChannel(nameof(PcSpeaker));
        _soundChannel.Volume = 100; // Full volume by default
        _soundChannel.StereoSeparation = 0; // PC Speaker is mono
        
        // Initialize filters for authentic PC speaker sound
        _highPassFilter.Setup(SampleRate, HighPassCutoffHz, FilterQ);
        _lowPassFilter.Setup(SampleRate, LowPassCutoffHz, FilterQ);
        
        ioPortDispatcher.AddIOPortHandler(PcSpeakerPortNumber, this);
        
        _pit8254Counter.SettingChangedEvent += OnPitSettingChanged;
        
        _deviceThread = new DeviceThread(nameof(PcSpeaker), PlaybackLoop, pauseHandler, loggerService);
    }
    
    private void OnPitSettingChanged(object? sender, EventArgs e) {
        // Only respond to changes in channel 2 (PC Speaker)
        if (_pit8254Counter.Index != 2) {
            return;
        }
        
        if (_prevPitMode != _pit8254Counter.Mode) {
            SetPITControl((PitMode)_pit8254Counter.Mode);
            _prevPitMode = _pit8254Counter.Mode;
        }
        
        SetCounter(_pit8254Counter.ReloadValue, _pit8254Counter.Mode);
    }
    
    private void PlaybackLoop() {
        Array.Clear(_audioBuffer, 0, _audioBuffer.Length);
        GenerateAudio();
        ApplyFiltersAndRender();
    }
    
    private void GenerateAudio() {
        if (!_portB.SpeakerOutput || !_portB.Timer2Gating || !_pitState.Mode3Counting) {
            // If speaker is disabled or gate is off, generate silence
            Array.Fill(_audioBuffer, 0.0f);
            return;
        }
        
        if (_pitState.Mode == PitMode.SquareWave || _pitState.Mode == PitMode.SquareWaveAlias) {
            GenerateSquareWave();
        } else {
            // For all other modes or inactive state, just use the current amplitude
            Array.Fill(_audioBuffer, _pitState.Amplitude);
        }
    }
    
    private void GenerateSquareWave() {
        // Calculate frequency from PIT counter value
        float counterMs = MsPerPitTick * _pit8254Counter.ReloadValue;
        if (counterMs <= 0) counterMs = 1.0f; // Avoid division by zero
        
        float cycleLength = (SampleRate * counterMs) / 1000.0f;
        if (cycleLength <= 2) cycleLength = 2; // Minimum cycle length
        
        // Calculate step per sample
        _cycleStep = 1.0f / cycleLength;
        
        // Generate square wave
        for (int i = 0; i < _audioBuffer.Length; i++) {
            // Advance cycle position
            _cyclePosition = (_cyclePosition + _cycleStep) % 1.0f;
            
            // Square wave: positive for first half, negative for second half
            _audioBuffer[i] = _cyclePosition < 0.5f ? PositiveAmplitude : NegativeAmplitude;
        }
    }
    
    private void ApplyFiltersAndRender() {
        // Apply high-pass filter to remove DC offset
        for (int i = 0; i < _audioBuffer.Length; i++) {
            _audioBuffer[i] = (float)_highPassFilter.Filter(_audioBuffer[i]);
        }
        
        // Apply low-pass filter to smooth the waveform
        for (int i = 0; i < _audioBuffer.Length; i++) {
            _audioBuffer[i] = (float)_lowPassFilter.Filter(_audioBuffer[i]);
        }
        _soundChannel.Render(_audioBuffer);
    }
    
    private void SetPITControl(PitMode mode) {
        _pitState.Mode = mode;
        
        switch (mode) {
            case PitMode.SquareWave:
            case PitMode.SquareWaveAlias:
                _pitState.Amplitude = PositiveAmplitude;
                // Start with mode3 not counting until triggered
                _pitState.Mode3Counting = false;
                break;
            
            case PitMode.OneShot:
                _pitState.Amplitude = PositiveAmplitude;
                _pitState.Mode1WaitingForCounter = true;
                _pitState.Mode1WaitingForTrigger = false;
                break;
            
            default:
                // For other modes, start with neutral amplitude
                _pitState.Amplitude = NeutralAmplitude;
                break;
        }
    }
    
    private void SetCounter(int counter, int pitModeValue) {
        PitMode mode = (PitMode)pitModeValue;
        
        float durationMs = MsPerPitTick * counter;
        
        // Adjust the PIT state based on the mode and counter
        switch (mode) {
            case PitMode.SquareWave:
            case PitMode.SquareWaveAlias:
                // Square wave mode - most common for PC speaker music
                if (counter < 2) {
                    // Too low for meaningful sound, disable counting
                    _pitState.Mode3Counting = false;
                    _pitState.Amplitude = PositiveAmplitude;
                    return;
                }
                
                // Update counter values
                _pitState.MaxMs = durationMs;
                _pitState.HalfMs = durationMs / 2;
                
                // Start counting if gate is enabled
                if (_portB.Timer2Gating) {
                    _pitState.Mode3Counting = true;
                }
                break;
                
            case PitMode.OneShot:
                _pitState.Mode1PendingMax = durationMs;
                if (_pitState.Mode1WaitingForCounter) {
                    _pitState.Mode1WaitingForCounter = false;
                    _pitState.Mode1WaitingForTrigger = true;
                }
                break;
                
            case PitMode.RateGenerator:
            case PitMode.RateGeneratorAlias:
                _pitState.MaxMs = durationMs;
                _pitState.HalfMs = MsPerPitTick; // Fixed half cycle time for rate generator
                break;
                
            case PitMode.SoftwareStrobe:
                _pitState.Amplitude = PositiveAmplitude;
                _pitState.MaxMs = durationMs;
                break;
                
            default:
                // For other modes, just update the duration
                _pitState.MaxMs = durationMs;
                break;
        }
        
        // Update the mode
        _pitState.Mode = mode;
    }
    
    public override byte ReadByte(ushort port) {
        // Port 0x61: PC Speaker control port
        // bit 0: Timer gate
        // bit 1: Speaker data
        // bits 2-7: Other system functions (unused here)
        byte result = (byte)(
            (_portB.Timer2Gating ? 0x01 : 0) | 
            (_portB.SpeakerOutput ? 0x02 : 0) | 
            0x3C // Unused bits are always 1
        );
        
        return result;
    }
    
    public override void WriteByte(ushort port, byte value) {
        _deviceThread.StartThreadIfNeeded();
        
        // Get previous state for edge detection
        bool oldTimer2Gating = _portB.Timer2Gating;
        
        // Update port B state
        _portB.Timer2Gating = (value & 0x01) != 0;
        _portB.SpeakerOutput = (value & 0x02) != 0;
        
        // Detect rising edge on timer gate (trigger)
        bool pitTrigger = !oldTimer2Gating && _portB.Timer2Gating;
        
        if (pitTrigger) {
            switch (_pitState.Mode) {
                case PitMode.OneShot:
                    if (!_pitState.Mode1WaitingForCounter) {
                        _pitState.Amplitude = NegativeAmplitude;
                        _pitState.Mode1WaitingForTrigger = false;
                        // Start the one-shot pulse
                    }
                    break;
                    
                case PitMode.SquareWave:
                case PitMode.SquareWaveAlias:
                    // Start square wave generation
                    _pitState.Mode3Counting = true;
                    _pitState.Amplitude = PositiveAmplitude;
                    // Reset cycle position to start at the beginning of the waveform
                    _cyclePosition = 0;
                    break;
                    
                default:
                    // Other modes not handled
                    break;
            }
        } else if (!_portB.Timer2Gating) {
            // Gate turned off
            if (_pitState.Mode == PitMode.SquareWave || _pitState.Mode == PitMode.SquareWaveAlias) {
                // Stop counting and set output high
                _pitState.Mode3Counting = false;
                _pitState.Amplitude = PositiveAmplitude;
            }
        }
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
                _pit8254Counter.SettingChangedEvent -= OnPitSettingChanged;
            }
            _disposed = true;
        }
    }
}