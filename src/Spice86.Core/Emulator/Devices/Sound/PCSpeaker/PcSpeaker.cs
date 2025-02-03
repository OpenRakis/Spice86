﻿namespace Spice86.Core.Emulator.Devices.Sound.PCSpeaker;

using Spice86.Core.Backend.Audio;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Collections.Concurrent;
using System.Diagnostics;

/// <summary>
/// Emulates a basic PC Speaker.
/// </summary>
public sealed class PcSpeaker : DefaultIOPortHandler, IDisposable {
    private const int PcSpeakerPortNumber = 0x61;
    
    /// <summary>
    /// Value into which the input frequency is divided to get the frequency in Hz.
    /// </summary>
    private const double FrequencyFactor = 1193180;
    private readonly SoundChannel _soundChannel;

    private readonly int _outputSampleRate = 48000;
    private readonly int _ticksPerSample;
    private readonly LatchedUInt16 _frequencyRegister;
    private readonly Stopwatch _durationTimer = new();
    private readonly ConcurrentQueue<QueuedNote> _queuedNotes = new();
    private readonly object _threadStateLock = new();
    private SpeakerControl _controlRegister = SpeakerControl.UseTimer;
    private Task? _generateWaveformTask;
    private readonly CancellationTokenSource _cancelGenerateWaveform = new();
    private int _currentPeriod;

    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="PcSpeaker"/>
    /// </summary>
    /// <param name="softwareMixer">The software mixer for sound channels.</param>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="ioPortDispatcher">The class that is responsible for dispatching ports reads and writes to classes that respond to them.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O port wasn't handled.</param>
    public PcSpeaker(SoftwareMixer softwareMixer, State state, IOPortDispatcher ioPortDispatcher,
        ILoggerService loggerService, bool failOnUnhandledPort) : base(state, failOnUnhandledPort, loggerService) {
        _soundChannel = softwareMixer.CreateChannel(nameof(PcSpeaker));
        _frequencyRegister = new();
        _frequencyRegister.ValueChanged += FrequencyChanged;
        _ticksPerSample = (int)(Stopwatch.Frequency / (double)_outputSampleRate);
        InitPortHandlers(ioPortDispatcher);
    }

    /// <summary>
    /// Gets the current frequency in Hz.
    /// </summary>
    private double Frequency => FrequencyFactor / _frequencyRegister;

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
        EnqueueCurrentNote();
        _currentPeriod = 0;
    }
    
    /// <summary>
    /// Invoked when the frequency has changed.
    /// </summary>
    /// <param name="source">Source of the event.</param>
    /// <param name="e">Unused EventArgs instance.</param>
    private void FrequencyChanged(object? source, EventArgs e) {
        EnqueueCurrentNote();

        _durationTimer.Reset();
        _durationTimer.Start();
        _currentPeriod = PeriodInSamples;
    }
    
    /// <summary>
    /// Enqueues the current note.
    /// </summary>
    private void EnqueueCurrentNote() {
        if (_durationTimer.IsRunning && _currentPeriod != 0) {
            _durationTimer.Stop();

            int periodDuration = _ticksPerSample * _currentPeriod;
            int repetitions = (int)(_durationTimer.ElapsedTicks / periodDuration);
            _queuedNotes.Enqueue(new QueuedNote(_currentPeriod, repetitions));

            lock (_threadStateLock) {
                if (_generateWaveformTask == null || _generateWaveformTask.IsCompleted) {
                    _generateWaveformTask = Task.Run(GenerateWaveformAsync);
                }
            }
        }
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
    
    /// <summary>
    /// Generates the PC speaker waveform.
    /// </summary>
    private async Task GenerateWaveformAsync() {
        FillWithSilence();

        byte[] buffer = new byte[4096];
        const bool expandToStereo = true;
        byte[] writeBuffer = new byte[buffer.Length * 2];

        int idleCount = 0;

        while (idleCount < 10000) {
            if (_queuedNotes.TryDequeue(out QueuedNote note)) {
                int samples = GenerateSquareWave(buffer, note.Period);
                int periods = note.PeriodCount;

                if (expandToStereo) {
                    ChannelAdapter.MonoToStereo(buffer.AsSpan(0, samples), writeBuffer.AsSpan(0, samples * 2));
                    samples *= 2;
                }

                while (periods > 0) {
                    _soundChannel.Render(writeBuffer.AsSpan(0, samples));
                    periods--;
                }

                GenerateSilence(buffer);
                idleCount = 0;
            } else {
                float[] floatArray = new float[buffer.Length];

                for (int i = 0; i < buffer.Length; i++) {
                    floatArray[i] = buffer[i];
                }


                while (_soundChannel.Render(floatArray.AsSpan()) > 0) {
                    await Task.Yield();
                }

                await Task.Delay(5, _cancelGenerateWaveform.Token);
                idleCount++;
            }

            _cancelGenerateWaveform.Token.ThrowIfCancellationRequested();
        }
    }

    private void FillWithSilence() {
        Span<float> buffer = stackalloc float[4096];
        _soundChannel.Render(buffer);
    }

    /// <inheritdoc />
    public override byte ReadByte(ushort port) {
        if (port != 0x61) {
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

        switch (port)
        {
            case 0x61:
            {
                SpeakerControl oldValue = _controlRegister;
                _controlRegister = (SpeakerControl)value;
                if ((oldValue & SpeakerControl.SpeakerOn) != 0 && (_controlRegister & SpeakerControl.SpeakerOn) == 0) {
                    SpeakerDisabled();
                }

                break;
            }
            case 0x42:
                _frequencyRegister.WriteByte(value);
                break;
            default:
                base.WriteByte(port, value);
                break;
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
    }

    private void Dispose(bool disposing) {
        if(!_disposed){
            if(disposing) {
                _frequencyRegister.ValueChanged -= FrequencyChanged;
                lock (_threadStateLock) {
                    _cancelGenerateWaveform.Cancel();
                }
            }
            _disposed = true;
        }
    }
}