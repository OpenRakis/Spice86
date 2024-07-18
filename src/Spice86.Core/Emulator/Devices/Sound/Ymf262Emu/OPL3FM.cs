﻿namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu;

using Spice86.Core.Backend.Audio;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

using System;

/// <summary>
/// Virtual device which emulates OPL3 FM sound.
/// </summary>
public sealed class OPL3FM : DefaultIOPortHandler, IDisposable {
    private const byte Timer1Mask = 0xC0;
    private const byte Timer2Mask = 0xA0;

    private readonly SoundChannel _soundChannel;
    private readonly FmSynthesizer? _synth;
    private int _currentAddress;
    private volatile bool _endThread;
    private readonly Thread _playbackThread;
    private bool _initialized;
    private byte _statusByte;
    private byte _timer1Data;
    private byte _timer2Data;
    private byte _timerControlByte;

    private bool _disposed;

    /// <summary>
    /// The sound channel used for the OPL3 FM synth.
    /// </summary>
    public SoundChannel SoundChannel => _soundChannel;

    /// <summary>
    /// Initializes a new instance of the OPL3 FM synth chip.
    /// </summary>
    /// <param name="fmSynthesizer">The OPL3's FM synthesizer. Contains the core logic for producing synthesized music.</param>
    /// <param name="fmSynthSoundChannel">The software mixer's sound channel for the OPL3 FM Synth chip.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O port wasn't handled.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public OPL3FM(FmSynthesizer fmSynthesizer, SoundChannel fmSynthSoundChannel, State state, bool failOnUnhandledPort, ILoggerService loggerService) : base(state, failOnUnhandledPort, loggerService) {
        _soundChannel = fmSynthSoundChannel;
        _synth = fmSynthesizer;
        _playbackThread = new Thread(GenerateWaveforms) {
            Name = nameof(OPL3FM)
        };
    }

    /// <inheritdoc />
    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(0x388, this);
        ioPortDispatcher.AddIOPortHandler(0x389, this);
    }

    /// <inheritdoc />
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if(!_disposed) {
            if(disposing) {
                _endThread = true;
                if (_playbackThread.IsAlive) {
                    _playbackThread.Join();
                }
                _initialized = false;
            }
            _disposed = true;
        }
    }

    /// <inheritdoc />
    public override byte ReadByte(int port) {
        if ((_timerControlByte & 0x01) != 0x00 && (_statusByte & Timer1Mask) == 0) {
            _timer1Data++;
            if (_timer1Data == 0) {
                _statusByte |= Timer1Mask;
            }
        }

        if ((_timerControlByte & 0x02) != 0x00 && (_statusByte & Timer2Mask) == 0) {
            _timer2Data++;
            if (_timer2Data == 0) {
                _statusByte |= Timer2Mask;
            }
        }

        return _statusByte;
    }

    /// <inheritdoc />
    public override ushort ReadWord(int port) {
        return _statusByte;
    }

    /// <inheritdoc />
    public override void WriteByte(int port, byte value) {
        if (port == 0x388) {
            _currentAddress = value;
        } else if (port == 0x389) {
            if (_currentAddress == 0x02) {
                _timer1Data = value;
            } else if (_currentAddress == 0x03) {
                _timer2Data = value;
            } else if (_currentAddress == 0x04) {
                _timerControlByte = value;
                if ((value & 0x80) == 0x80) {
                    _statusByte = 0;
                }
            } else {
                if (!_initialized) {
                    StartPlaybackThread();
                }

                _synth?.SetRegisterValue(0, _currentAddress, value);
            }
        }
    }

    /// <inheritdoc />
    public override void WriteWord(int port, ushort value) {
        if (port == 0x388) {
            WriteByte(0x388, (byte)value);
            WriteByte(0x389, (byte)(value >> 8));
        }
    }
    /// <summary>
    /// Generates and plays back output waveform data.
    /// </summary>
    private void GenerateWaveforms() {
        const int length = 1024;
        Span<float> buffer = stackalloc float[length];
        Span<float> playBuffer = stackalloc float[length * 2];
        FillBuffer(buffer, playBuffer);
        while (!_endThread) {
            _soundChannel.Render(playBuffer);
            FillBuffer(buffer, playBuffer);
        }
    }

    private void FillBuffer(Span<float> buffer, Span<float> playBuffer) {
        _synth?.GetData(buffer);
        ChannelAdapter.MonoToStereo(buffer, playBuffer);
    }

    private void StartPlaybackThread() {
        if(!_endThread) {
            _playbackThread.Start();
            _initialized = true;
        }
    }
}