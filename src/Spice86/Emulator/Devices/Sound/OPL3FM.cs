namespace Spice86.Emulator.Devices.Sound;

using Spice86.Emulator.IOPorts;
using Spice86.Emulator.Sound;
using Spice86.Emulator.VM;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using MicroAudio;

using Ymf262Emu;

/// <summary>
/// Virtual device which emulates OPL3 FM sound.
/// </summary>
public sealed class OPL3FM : DefaultIOPortHandler {
    private const byte Timer1Mask = 0xC0;
    private const byte Timer2Mask = 0xA0;

    private readonly AudioPlayer? _audioPlayer;
    private readonly FmSynthesizer? _synth;
    private int _currentAddress;
    private volatile bool _endThread;
    private System.Threading.Thread _generateThread;
    private bool _initialized;
    private bool _paused;
    private byte _statusByte;
    private byte _timer1Data;
    private byte _timer2Data;
    private byte _timerControlByte;

    public OPL3FM(Machine machine, Configuration configuration) : base(machine, configuration) {
        if (configuration.CreateAudioBackend) {
            _audioPlayer = Audio.CreatePlayer();
        }
        if (_audioPlayer is not null) {
            this._synth = new FmSynthesizer(this._audioPlayer.Format.SampleRate);
        }
        this._generateThread = new System.Threading.Thread(this.GenerateWaveforms) {
            IsBackground = true,
            Priority = System.Threading.ThreadPriority.AboveNormal
        };
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(0x388, this);
        ioPortDispatcher.AddIOPortHandler(0x389, this);
    }

    public void Dispose() {
        if (this._initialized) {
            if (!_paused) {
                this._endThread = true;
                this._generateThread.Join();
            }
            this._audioPlayer?.Dispose();
            this._initialized = false;
        }
    }

    public void Pause() {
        if (this._initialized && !this._paused) {
            this._endThread = true;
            this._generateThread.Join();
            this._paused = true;
        }
    }

    public override byte ReadByte(int port) {
        if ((this._timerControlByte & 0x01) != 0x00 && (this._statusByte & Timer1Mask) == 0) {
            this._timer1Data++;
            if (this._timer1Data == 0) {
                this._statusByte |= Timer1Mask;
            }
        }

        if ((this._timerControlByte & 0x02) != 0x00 && (this._statusByte & Timer2Mask) == 0) {
            this._timer2Data++;
            if (this._timer2Data == 0) {
                this._statusByte |= Timer2Mask;
            }
        }

        return this._statusByte;
    }

    public override ushort ReadWord(int port) {
        return this._statusByte;
    }

    public void Resume() {
        if (_paused) {
            this._endThread = false;
            this._generateThread = new System.Threading.Thread(this.GenerateWaveforms) { IsBackground = true };
            this._generateThread.Start();
            this._paused = false;
        }
    }

    public override void WriteByte(int port, byte value) {
        if (port == 0x388) {
            _currentAddress = value;
        } else if (port == 0x389) {
            if (_currentAddress == 0x02) {
                this._timer1Data = value;
            } else if (_currentAddress == 0x03) {
                this._timer2Data = value;
            } else if (_currentAddress == 0x04) {
                this._timerControlByte = value;
                if ((value & 0x80) == 0x80) {
                    this._statusByte = 0;
                }
            } else {
                if (!this._initialized) {
                    this.Initialize();
                }

                this._synth?.SetRegisterValue(0, _currentAddress, value);
            }
        }
    }

    public override void WriteWord(int port, ushort value) {
        if (port == 0x388) {
            WriteByte(0x388, (byte)value);
            this.WriteByte(0x389, (byte)(value >> 8));
        }
    }

    /// <summary>
    /// Generates and plays back output waveform data.
    /// </summary>
    private void GenerateWaveforms() {
        float[]? buffer = new float[1024];
        float[] playBuffer;
        if (_audioPlayer is not null) {
            bool expandToStereo = this._audioPlayer.Format.Channels == 2;
            if (expandToStereo) {
                playBuffer = new float[buffer.Length * 2];
            } else {
                playBuffer = buffer;
            }

            this._audioPlayer.BeginPlayback();
            fillBuffer();
            while (!_endThread) {
                Audio.WriteFullBuffer(this._audioPlayer, playBuffer);
                fillBuffer();
            }

            this._audioPlayer.StopPlayback();

            void fillBuffer() {
                this._synth?.GetData(buffer);
                if (expandToStereo) {
                    ChannelAdapter.MonoToStereo(buffer.AsSpan(), playBuffer.AsSpan());
                }
            }
        }
    }

    /// <summary>
    /// Performs DirectSound initialization.
    /// </summary>
    private void Initialize() {
        this._generateThread.Start();
        this._initialized = true;
    }
}