namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Backend.Audio;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Sound;
using Spice86.Core.Emulator.Devices.Sound.Ymf262Emu;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using System;

/// <summary>
/// Virtual device which emulates OPL3 FM sound.
/// </summary>
public class OPL3FM : DefaultIOPortHandler, IDisposable {
    private const byte Timer1Mask = 0xC0;
    private const byte Timer2Mask = 0xA0;

    protected readonly AudioPlayer? _audioPlayer;
    protected readonly FmSynthesizer? _synth;
    private int _currentAddress;
    protected volatile bool _endThread;
    protected readonly Thread _playbackThread;
    protected bool _initialized;
    private bool _paused;
    protected byte _statusByte;
    private byte _timer1Data;
    private byte _timer2Data;
    private byte _timerControlByte;

    private bool _disposed;
    
    protected readonly double MsPerFrame = 0d;

    /// <summary>
    /// Initializes a new instance of the OPL3 FM synth chip.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="configuration">The emulator configuration.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public OPL3FM(Machine machine, Configuration configuration, ILoggerService loggerService) : base(machine, configuration, loggerService) {
        _audioPlayer = Audio.CreatePlayer(48000, 2048);
        if (_audioPlayer is not null) {
            _synth = new FmSynthesizer(_audioPlayer.Format.SampleRate);
        }
        _playbackThread = new Thread(RnderWaveFormOnPlaybackThread);
    }

    /// <inheritdoc />
    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(OPLConsts.FmMusicStatusPortNumber2, this);
        ioPortDispatcher.AddIOPortHandler(OPLConsts.FmMusicDataPortNumber2, this);
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
                _audioPlayer?.Dispose();
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
    protected virtual void RnderWaveFormOnPlaybackThread() {
        if (_audioPlayer is null) {
            return;
        }
        int length = 1024;
        Span<float> buffer = stackalloc float[length];
        bool expandToStereo = _audioPlayer.Format.Channels == 2;
        if (expandToStereo) {
            length *= 2;
        }
        Span<float> playBuffer = stackalloc float[length];
        FillBuffer(buffer, playBuffer, expandToStereo);
        while (!_endThread) {
            Audio.WriteFullBuffer(_audioPlayer, playBuffer);
            FillBuffer(buffer, playBuffer, expandToStereo);
        }
    }

    private void FillBuffer(Span<float> buffer, Span<float> playBuffer, bool expandToStereo) {
        _synth?.GetData(buffer);
        if (expandToStereo) {
            ChannelAdapter.MonoToStereo(buffer, playBuffer);
        }
    }

    public void StartPlayback(string threadName) {
        if (!_initialized) {
            StartPlaybackThread(threadName);
        }
    }

    protected void StartPlaybackThread(string threadName = "") {
        if(!_endThread) {
            if(!string.IsNullOrWhiteSpace(threadName)) {
                _playbackThread.Name = threadName;
            }
            _playbackThread.Start();
            _initialized = true;
        }
    }
}