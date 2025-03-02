namespace Spice86.Core.Emulator.Devices.Sound.PCSpeaker;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Diagnostics;

/// <summary>
/// Emulates a basic PC Speaker.
/// </summary>
public sealed class PcSpeaker : DefaultIOPortHandler, IDisposable {
    private const int PcSpeakerPortNumber = 0x61;

    private readonly SoundChannel _soundChannel;

    private readonly int _outputSampleRate = 48000;
    private readonly int _ticksPerSample;
    private readonly Pit8254Counter _pit8254Counter;
    private readonly Stopwatch _durationTimer = new();
    private QueuedNote _currentNote;
    private SpeakerControl _controlRegister = SpeakerControl.UseTimer;
    private int _currentPeriod;

    private readonly Thread _playbackThread;
    private bool _playbackStarted;
    private volatile bool _endPlayback;
    private readonly IPauseHandler _pauseHandler;

    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="PcSpeaker"/>
    /// </summary>
    /// <param name="softwareMixer">The software mixer for sound channels.</param>
    /// <param name="pit8254Counter">PIT8254 counter backing up the PC speaker.</param>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="ioPortDispatcher">The class that is responsible for dispatching ports reads and writes to classes that respond to them.</param>
    /// <param name="pauseHandler">The handler for the emulation pause state.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O port wasn't handled.</param>
    public PcSpeaker(SoftwareMixer softwareMixer,
        State state,
        Pit8254Counter pit8254Counter,
        IOPortDispatcher ioPortDispatcher,
        IPauseHandler pauseHandler,
        ILoggerService loggerService,
        bool failOnUnhandledPort) : base(state, failOnUnhandledPort, loggerService) {
        _soundChannel = softwareMixer.CreateChannel(nameof(PcSpeaker));
        _pit8254Counter = pit8254Counter;
        pit8254Counter.SettingChangedEvent += OnTimerSettingChanged;
        _ticksPerSample = (int)(Stopwatch.Frequency / (double)_outputSampleRate);
        InitPortHandlers(ioPortDispatcher);
        _pauseHandler = pauseHandler;
        _playbackThread = new Thread(AudioPlayback) {
            Name = nameof(PcSpeaker),
        };
    }

    /// <summary>
    /// Gets the current frequency in Hz.
    /// </summary>
    private double Frequency => _pit8254Counter.Activator.Frequency;

    /// <summary>
    /// Gets the current period in samples.
    /// </summary>
    private int PeriodInSamples => (int)(_outputSampleRate / Frequency);

    /// <summary>
    /// Fills a buffer with silence.
    /// </summary>
    /// <param name="buffer">Buffer to fill.</param>
    private static void GenerateSilence(Span<byte> buffer) => buffer.Fill(127);

    /// <summary>
    /// Invoked when the speaker has been turned off.
    /// </summary>
    private void SpeakerDisabled() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("PC Speaker disabled");
        }
        EnqueueCurrentNote();
        _currentPeriod = 0;
    }

    /// <summary>
    /// Invoked when the frequency has changed.
    /// </summary>
    /// <param name="source">Source of the event.</param>
    /// <param name="e">Unused EventArgs instance.</param>
    private void OnTimerSettingChanged(object? source, EventArgs e) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("PC Speaker frequency changed to {Frequency}", _pit8254Counter.Activator.Frequency);
        }
        EnqueueCurrentNote();

        _durationTimer.Reset();
        _durationTimer.Start();
        _currentPeriod = PeriodInSamples;
    }

    /// <summary>
    /// Enqueues the current note.
    /// </summary>
    private void EnqueueCurrentNote() {
        if (!_durationTimer.IsRunning || _currentPeriod == 0) {
            return;
        }
        _durationTimer.Stop();

        int periodDuration = _ticksPerSample * _currentPeriod;
        int repetitions = (int)(_durationTimer.ElapsedTicks / periodDuration);
        _currentNote = new QueuedNote(_currentPeriod, repetitions);
    }

    /// <summary>
    /// Fills a buffer with a square wave of the current frequency.
    /// </summary>
    /// <param name="buffer">Buffer to fill.</param>
    /// <param name="period">The number of samples in the period.</param>
    /// <returns>Number of bytes written to the buffer.</returns>
    private int GenerateSquareWave(Span<byte> buffer, int period) {
        if (period < 2) {
            buffer[0] = 127;
            return 1;
        }

        int halfPeriod = period / 2;
        buffer[..halfPeriod].Fill(96);
        buffer.Slice(halfPeriod, halfPeriod).Fill(120);

        return period;
    }

    private void AudioPlayback() {
        byte[] buffer = new byte[4096];
        byte[] writeBuffer = new byte[buffer.Length * 2];

        while (!_endPlayback) {
            _pauseHandler.WaitIfPaused();
            if (!IsEnabled(_controlRegister)) {
                continue;
            }
            int samples = GenerateSquareWave(buffer, _currentNote.Period);
            int periods = _currentNote.PeriodCount;

            // While the original PC speaker was mono, the emulator output is stereo.
            // So we have to duplicate the signal.
            ChannelAdapter.MonoToStereo(buffer.AsSpan(0, samples), writeBuffer.AsSpan(0, samples * 2));
            samples *= 2;

            while (periods > 0) {
                _soundChannel.Render(writeBuffer.AsSpan(0, samples));
                periods--;
            }

            GenerateSilence(buffer);
        }
    }

    /// <inheritdoc />
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

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(PcSpeakerPortNumber, this);
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("PC Speaker set value {PCSpeakerValue}", ConvertUtils.ToHex8(value));
        }

        if (port == PcSpeakerPortNumber) {
            SpeakerControl newValue = (SpeakerControl)value;
            if (IsEnabled(_controlRegister) && !IsEnabled(newValue)) {
                SpeakerDisabled();
            } else if (!_playbackStarted) {
                _loggerService.Information("Starting thread '{ThreadName}'", _playbackThread.Name);
                _playbackStarted = true;
                _playbackThread.Start();
            }
            _controlRegister = newValue;
        } else {
            base.WriteByte(port, value);
        }
    }

    private bool IsEnabled(SpeakerControl registerValue) {
        return (registerValue & SpeakerControl.SpeakerOn) != 0 && (registerValue & SpeakerControl.UseTimer) != 0;
    }

    /// <inheritdoc />
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
    }

    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _pit8254Counter.SettingChangedEvent -= OnTimerSettingChanged;
                _endPlayback = true;
                if (_playbackThread.IsAlive) {
                    _playbackThread.Join();
                }
            }
            _disposed = true;
        }
    }
}