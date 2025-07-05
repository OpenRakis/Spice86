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
public sealed class PcSpeaker : DefaultIOPortHandler, IDisposable {
    public const int PcSpeakerPortNumber = 0x61;
    public const int SampleRate = 48000;
    public const int FramesPerBuffer = 512;
    public const float PitTickRate = 1193182.0f;
    public const float MsPerPitTick = 1000.0f / PitTickRate;
    
    public const float PositiveAmplitude = 0.5f;
    public const float NegativeAmplitude = -0.5f;
    public const float NeutralAmplitude = 0.0f;

    public const int HighPassCutoffHz = 100;  // Filter out DC and very low frequencies
    public const int LowPassCutoffHz = 8000;  // Filter out high frequencies
    public const double FilterQ = 0.7071;     // Standard Butterworth Q factor

    private readonly Pit8254Counter _pit8254Counter;
    private readonly SoundChannel _soundChannel;
    private readonly DeviceThread _deviceThread;
    private bool _disposed;
    
    private readonly PpiPortB _portB = new();
    
    private readonly float[] _audioBuffer;
    
    // Audio cycle tracking
    private float _cyclePosition = 0;
    private float _cycleStep = 0;
    
    private readonly LowPass _lowPassFilter = new();
    private readonly HighPass _highPassFilter = new();
    
    // Current amplitude state
    private float _currentAmplitude = PositiveAmplitude;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="PcSpeaker"/> class.
    /// </summary>
    public PcSpeaker(
        SoftwareMixer softwareMixer, State state, Pit8254Counter pit8254Counter,
        IOPortDispatcher ioPortDispatcher, IPauseHandler pauseHandler,
        ILoggerService loggerService, bool failOnUnhandledPort)
        : base(state, failOnUnhandledPort, loggerService) {
        _pit8254Counter = pit8254Counter;
        _audioBuffer = new float[FramesPerBuffer];
        _soundChannel = softwareMixer.CreateChannel(nameof(PcSpeaker));
        _soundChannel.Volume = 100; // Full volume by default
        _soundChannel.StereoSeparation = 0; // PC Speaker is mono
        
        // Initialize filters for authentic PC speaker sound
        _highPassFilter.Setup(SampleRate, HighPassCutoffHz, FilterQ);
        _lowPassFilter.Setup(SampleRate, LowPassCutoffHz, FilterQ);
        
        ioPortDispatcher.AddIOPortHandler(PcSpeakerPortNumber, this);
        
        // Subscribe to PIT events
        _pit8254Counter.SettingChangedEvent += OnPitSettingChanged;
        _pit8254Counter.GateStateChanged += OnPitGateChanged;
        
        _deviceThread = new DeviceThread(nameof(PcSpeaker), PlaybackLoop, pauseHandler, loggerService);
    }
    
    private void OnPitSettingChanged(object? sender, EventArgs e) {
        // Only respond to changes in channel 2 (PC Speaker)
        if (_pit8254Counter.Index != 2) {
            return;
        }
        
        // Update cycle generation parameters based on PIT counter value
        UpdateCycleParameters();
    }
    
    private void OnPitGateChanged(object? sender, bool enabled) {
        // Only respond to changes in channel 2 (PC Speaker)
        if (_pit8254Counter.Index != 2) {
            return;
        }
        
        // No action needed here - the gate state is checked in GenerateAudio
    }
    
    private void UpdateCycleParameters() {
        // Calculate frequency from PIT counter value
        float counterMs = MsPerPitTick * _pit8254Counter.ReloadValue;
        if (counterMs <= 0) counterMs = 1.0f; // Avoid division by zero
        
        float cycleLength = (SampleRate * counterMs) / 1000.0f;
        if (cycleLength <= 2) cycleLength = 2; // Minimum cycle length
        
        // Calculate step per sample
        _cycleStep = 1.0f / cycleLength;
    }
    
    private void PlaybackLoop() {
        Array.Clear(_audioBuffer, 0, _audioBuffer.Length);
        GenerateAudio();
        ApplyFiltersAndRender();
    }
    
    private void GenerateAudio() {
        if (!_portB.SpeakerOutput || !_portB.Timer2Gating || !_pit8254Counter.IsSquareWaveActive) {
            // If speaker is disabled or gate is off, generate silence
            Array.Fill(_audioBuffer, 0.0f);
            return;
        }
        
        if (_pit8254Counter.CurrentPitMode == Pit8254Counter.PitMode.SquareWave || 
            _pit8254Counter.CurrentPitMode == Pit8254Counter.PitMode.SquareWaveAlias) {
            GenerateSquareWave();
        } else {
            // For all other modes or inactive state, just use the current amplitude
            Array.Fill(_audioBuffer, _currentAmplitude);
        }
    }
    
    private void GenerateSquareWave() {
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
    
    /// <summary>
    /// Reads a byte from the specified I/O port.
    /// </summary>
    /// <remarks>When reading from port <see cref="PcSpeakerPortNumber"/>, the returned byte contains the
    /// following bit fields: <list type="bullet"> <item><description>Bit 0: Timer gate state (1 if enabled, 0 if
    /// disabled).</description></item> <item><description>Bit 1: Speaker output state (1 if active, 0 if
    /// inactive).</description></item> <item><description>Bits 2-7: Reserved and always set to 1.</description></item>
    /// </list> For other ports, the behavior is delegated to the base implementation.</remarks>
    /// <param name="port">The I/O port address to read from.</param>
    /// <returns>A byte representing the state of the specified port. For port <see cref="PcSpeakerPortNumber"/>,  the returned
    /// value encodes the PC Speaker control state, including timer gating and speaker output. For other ports, the
    /// result is determined by the base implementation.</returns>
    public override byte ReadByte(ushort port) {
        if (port != PcSpeakerPortNumber) {
            return base.ReadByte(port);
        }
        // Port 0x61: PC Speaker control port
        // bit 0: Timer gate
        // bit 1: Speaker data
        // bits 2-7: Other system functions (unused here)
        byte result = (byte)(
            (_portB.Timer2Gating ? 0x01 : 0) |
            (_portB.SpeakerOutput ? 0x02 : 0) |
            0b111100 // Unused bits are always 1
        );

        return result;
    }

    /// <summary>
    /// Writes a byte to the specified port, updating the state of the PC speaker if the port matches the PC speaker
    /// port number.
    /// </summary>
    /// <remarks>If the specified port is not the PC speaker port number, the method delegates the operation
    /// to the base implementation. For the PC speaker port, this method updates the speaker's state, including its
    /// timer gating and output settings. Changes to the timer gate state are detected and propagated to the associated
    /// timer via <see cref="Pit8254Counter.SetGateState(bool)"/> .</remarks>
    /// <param name="port">The port number to which the byte is written. Must be a valid port number.</param>
    /// <param name="value">The byte value to write to the port. The value may affect the PC speaker state if the port matches the PC
    /// speaker port number.</param>
    public override void WriteByte(ushort port, byte value) {
        if (port != PcSpeakerPortNumber) {
            base.WriteByte(port, value);
            return;
        }
        
        _deviceThread.StartThreadIfNeeded();
        
        // Get previous state for edge detection
        bool oldTimer2Gating = _portB.Timer2Gating;
        
        // Update port B state
        _portB.Timer2Gating = (value & 0x01) != 0;
        _portB.SpeakerOutput = (value & 0x02) != 0;
        
        // Detect changes to the timer gate
        if (oldTimer2Gating != _portB.Timer2Gating) {
            _pit8254Counter.SetGateState(_portB.Timer2Gating);
        }
        
        // Update the cycle parameters in case they've changed
        UpdateCycleParameters();
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
                _pit8254Counter.GateStateChanged -= OnPitGateChanged;
            }
            _disposed = true;
        }
    }
}