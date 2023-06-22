namespace Spice86.Core.Emulator.Devices.Sound;

using System.Threading;

using Serilog.Events;

using Spice86.Core.Backend.Audio;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Sound;
using Spice86.Core.Emulator.Sound.Blaster;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Sound blaster implementation. <br/>
/// http://www.fysnet.net/detectsb.htm
/// </summary>
public sealed class SoundBlaster : DefaultIOPortHandler, IDmaDevice8, IDmaDevice16, IRequestInterrupt, IBlasterEnvVarProvider, IDisposable {
    /// <summary>
    /// The port number for checking if data is available to be read from the DSP.
    /// </summary>
    public const int DSP_DATA_AVAILABLE_PORT_NUMBER = 0x22E;

    /// <summary>
    /// The port number for reading data from the DSP.
    /// </summary>
    public const int DSP_READ_PORT_NUMBER = 0x22A;

    /// <summary>
    /// The port number for resetting the DSP.
    /// </summary>
    public const int DSP_RESET_PORT_NUMBER = 0x226;

    /// <summary>
    /// The port number for checking the status of the DSP write buffer.
    /// </summary>
    public const int DSP_WRITE_BUFFER_STATUS_PORT_NUMBER = 0x22C;

    /// <summary>
    /// The port number for sending FM music data to the left FM music channel.
    /// </summary>
    public const int FM_MUSIC_DATA_PORT_NUMBER = 0x229;

    /// <summary>
    /// The port number for sending FM music data to the right FM music channel.
    /// </summary>
    public const int FM_MUSIC_DATA_PORT_NUMBER_2 = 0x389;

    /// <summary>
    /// The port number for checking the status of the left FM music channel.
    /// </summary>
    public const int FM_MUSIC_STATUS_PORT_NUMBER = 0x228;

    /// <summary>
    /// The port number for checking the status of the right FM music channel.
    /// </summary>
    public const int FM_MUSIC_STATUS_PORT_NUMBER_2 = 0x388;

    /// <summary>
    /// The port number for sending data to the left speaker.
    /// </summary>
    public const int LEFT_SPEAKER_DATA_PORT_NUMBER = 0x221;

    /// <summary>
    /// The port number for checking the status of the left speaker.
    /// </summary>
    public const int LEFT_SPEAKER_STATUS_PORT_NUMBER = 0x220;

    /// <summary>
    /// The port number for sending data to the mixer.
    /// </summary>
    public const int MIXER_DATA_PORT_NUMBER = 0x225;

    /// <summary>
    /// The port number for accessing the mixer registers.
    /// </summary>
    public const int MIXER_REGISTER_PORT_NUMBER = 0x224;

    /// <summary>
    /// The port number for sending data to the right speaker.
    /// </summary>
    public const int RIGHT_SPEAKER_DATA_PORT_NUMBER = 0x223;

    /// <summary>
    /// The port number for checking the status of the right speaker.
    /// </summary>
    public const int RIGHT_SPEAKER_STATUS_PORT_NUMBER = 0x222;

    private bool _disposed;

    private static readonly SortedList<byte, byte> CommandLengths = new() {
        [Commands.SetTimeConstant] = 1,
        [Commands.SingleCycleDmaOutput8] = 2,
        [Commands.DspIdentification] = 1,
        [Commands.SetBlockTransferSize] = 2,
        [Commands.SetSampleRate] = 2,
        [Commands.SetInputSampleRate] = 2,
        [Commands.SingleCycleDmaOutput16] = 3,
        [Commands.AutoInitDmaOutput16] = 3,
        [Commands.SingleCycleDmaOutput16Fifo] = 3,
        [Commands.AutoInitDmaOutput16Fifo] = 3,
        [Commands.SingleCycleDmaOutput8_Alt] = 3,
        [Commands.AutoInitDmaOutput8_Alt] = 3,
        [Commands.SingleCycleDmaOutput8Fifo_Alt] = 3,
        [Commands.AutoInitDmaOutput8Fifo_Alt] = 3,
        [Commands.PauseForDuration] = 2,
        [Commands.SingleCycleDmaOutputADPCM4Ref] = 2,
        [Commands.SingleCycleDmaOutputADPCM2Ref] = 2,
        [Commands.SingleCycleDmaOutputADPCM3Ref] = 2
    };

    private readonly List<byte> _commandData = new();
    private readonly int _dma16;
    private readonly DmaChannel _dmaChannel;
    private readonly Dsp _dsp;
    private readonly Mixer _mixer;
    private readonly Queue<byte> _outputData = new();
    private readonly Thread _playbackThread;
    private bool _blockTransferSizeSet;
    private byte _commandDataLength;
    private byte _currentCommand;
    private volatile bool _endPlayback;
    private int _pauseDuration;
    private BlasterState _state;
    private bool _playbackStarted;

    /// <summary>
    /// Initializes a new instance of the SoundBlaster class.
    /// </summary>
    /// <param name="machine">Virtual machine instance associated with the device.</param>
    /// <param name="configuration">The emulator config.</param>
    /// <param name="loggerService">The logging service for events such as non-fatal errors, warnings, or information</param>
    public SoundBlaster(Machine machine, Configuration configuration, ILoggerService loggerService, SoundBlasterHardwareConfig soundBlasterHardwareConfig) : base(machine, configuration, loggerService) {
        IRQ = soundBlasterHardwareConfig.Irq;
        DMA = soundBlasterHardwareConfig.LowDma;
        _dma16 = soundBlasterHardwareConfig.HighDma;
        _mixer = new Mixer(this);
        _dmaChannel = machine.DmaSubsystem.DmaController.Channels[DMA];
        _dsp = new Dsp(machine, this, DMA, _dma16);
        _playbackThread = new Thread(AudioPlayback) {
            Name = "PCMAudio",
            Priority = ThreadPriority.AboveNormal
        };
    }

    /// <summary>
    /// The BLASTER environment variable
    /// </summary>
    public string BlasterString => $"A220 I{IRQ} D{DMA} T4";

    /// <inheritdoc />
    public override byte ReadByte(int port) {
        switch (port) {
            case DspPorts.DspReadData:
                if (_outputData.Count > 0) {
                    return _outputData.Dequeue();
                } else {
                    return 0;
                }

            case DspPorts.DspWrite:
                return 0x00;

            case DspPorts.DspReadBufferStatus:
                if (_mixer.InterruptStatusRegister == InterruptStatus.Dma8) {
                }

                _mixer.InterruptStatusRegister = InterruptStatus.None;
                return _outputData.Count > 0 ? (byte)0x80 : (byte)0u;

            case DspPorts.MixerAddress:
                return (byte)_mixer.CurrentAddress;

            case DspPorts.MixerData:
                return _mixer.ReadData();
        }
        return 0;
    }

    /// <inheritdoc />
    public override void WriteByte(int port, byte value) {
        if (!_playbackStarted) {
            _playbackThread.Start();
            _playbackStarted = true;
        }
        switch (port) {
            case DspPorts.DspReset:
                // Expect a 1, then 0 written to reset the DSP.
                if (value == 1) {
                    _state = BlasterState.ResetRequest;
                } else if (value == 0 && _state == BlasterState.ResetRequest) {
                    _state = BlasterState.Resetting;
                    Reset();
                }
                break;

            case DspPorts.DspWrite:
                if (_state == BlasterState.WaitingForCommand) {
                    _currentCommand = value;
                    _state = BlasterState.ReadingCommand;
                    _commandData.Clear();
                    CommandLengths.TryGetValue(value, out _commandDataLength);
                    if (_commandDataLength == 0) {
                        if (!ProcessCommand()) {
                            base.WriteByte(port, value);
                        }
                    }
                } else if (_state == BlasterState.ReadingCommand) {
                    _commandData.Add(value);
                    if (_commandData.Count >= _commandDataLength) {
                        if (!ProcessCommand()) {
                            base.WriteByte(port, value);
                        }
                    }
                }
                break;

            case DspPorts.MixerAddress:
                _mixer.CurrentAddress = value;
                break;
        }
    }

    /// <inheritdoc />
    public override ushort ReadWord(int port) {
        uint value = ReadByte(port);
        value |= (uint)(ReadByte(port + 1) << 8);
        return (ushort)value;
    }

    int IDmaDevice8.Channel => DMA;

    int IDmaDevice16.Channel => _dma16;

    /// <summary>
    /// Gets the DMA channel assigned to the device.
    /// </summary>
    public int DMA { get; }

    /// <summary>
    /// The list of input ports.
    /// </summary>
    public IEnumerable<int> InputPorts => new int[] { DspPorts.DspReadData, DspPorts.DspWrite, DspPorts.DspReadBufferStatus, DspPorts.MixerAddress, DspPorts.MixerData };

    /// <summary>
    /// Gets the hardware IRQ assigned to the device.
    /// </summary>
    public byte IRQ { get; }

    /// <summary>
    /// The list of output ports.
    /// </summary>
    public IEnumerable<int> OutputPorts => new int[] { DspPorts.DspReset, DspPorts.DspWrite, DspPorts.MixerAddress };

    /// <inheritdoc />
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if(!_disposed) {
            if(disposing) {
                _endPlayback = true;
                if (_playbackThread.IsAlive) {
                    _playbackThread.Join();
                }
            }
            _disposed = true;
        }
    }

    /// <inheritdoc />
    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(DSP_RESET_PORT_NUMBER, this);
        ioPortDispatcher.AddIOPortHandler(DSP_WRITE_BUFFER_STATUS_PORT_NUMBER, this);
        ioPortDispatcher.AddIOPortHandler(MIXER_REGISTER_PORT_NUMBER, this);
        ioPortDispatcher.AddIOPortHandler(MIXER_DATA_PORT_NUMBER, this);
        ioPortDispatcher.AddIOPortHandler(DSP_DATA_AVAILABLE_PORT_NUMBER, this);
        ioPortDispatcher.AddIOPortHandler(DSP_READ_PORT_NUMBER, this);

        ioPortDispatcher.AddIOPortHandler(LEFT_SPEAKER_STATUS_PORT_NUMBER, this);
        ioPortDispatcher.AddIOPortHandler(LEFT_SPEAKER_DATA_PORT_NUMBER, this);
        ioPortDispatcher.AddIOPortHandler(RIGHT_SPEAKER_STATUS_PORT_NUMBER, this);
        ioPortDispatcher.AddIOPortHandler(RIGHT_SPEAKER_DATA_PORT_NUMBER, this);
        ioPortDispatcher.AddIOPortHandler(FM_MUSIC_STATUS_PORT_NUMBER, this);
        ioPortDispatcher.AddIOPortHandler(FM_MUSIC_DATA_PORT_NUMBER, this);
        // Those are managed by OPL3FM class.
        //ioPortDispatcher.AddIOPortHandler(FM_MUSIC_STATUS_PORT_NUMBER_2, this);
        //ioPortDispatcher.AddIOPortHandler(FM_MUSIC_DATA_PORT_NUMBER_2, this);
    }

    void IDmaDevice8.SingleCycleComplete() {
        _dsp.IsEnabled = false;
        RaiseInterruptRequest();
    }

    void IDmaDevice16.SingleCycleComplete() => throw new NotImplementedException();

    int IDmaDevice8.WriteBytes(ReadOnlySpan<byte> source) => _dsp.DmaWrite(source);

    int IDmaDevice16.WriteWords(IntPtr source, int count) => throw new NotImplementedException();
    
    private void AudioPlayback() {
        using AudioPlayer? player = Audio.CreatePlayer(48000, 2048);
        if (player is null) {
            return;
        }
        Span<byte> buffer = stackalloc byte[512];
        short[] writeBuffer = new short[65536 * 2];
        int sampleRate = player.Format.SampleRate;

        while (!_endPlayback) {
            _dsp.Read(buffer);
            int length;
            if (_dsp.Is16Bit && _dsp.IsStereo) {
                length = LinearUpsampler.Resample16Stereo(_dsp.SampleRate, sampleRate, buffer.Cast<byte, short>(), writeBuffer);
            } else if (_dsp.Is16Bit) {
                length = LinearUpsampler.Resample16Mono(_dsp.SampleRate, sampleRate, buffer.Cast<byte, short>(), writeBuffer);
            } else if (_dsp.IsStereo) {
                length = LinearUpsampler.Resample8Stereo(_dsp.SampleRate, sampleRate, buffer, writeBuffer);
            } else {
                length = LinearUpsampler.Resample8Mono(_dsp.SampleRate, sampleRate, buffer, writeBuffer);
            }

            Audio.WriteFullBuffer(player, writeBuffer.AsSpan(0, length));

            if (_machine.IsPaused) {
                while (_machine.IsPaused) {
                    Thread.Sleep(1);
                    if (_endPlayback) {
                        return;
                    }
                }
            }

            if (_pauseDuration > 0) {
                Array.Clear(writeBuffer, 0, writeBuffer.Length);
                int count = (_pauseDuration / (1024 / 2)) + 1;
                for (int i = 0; i < count; i++) {
                    Audio.WriteFullBuffer(player, writeBuffer.AsSpan(0, 1024));
                }

                _pauseDuration = 0;
                RaiseInterruptRequest();
            }
        }
    }

    /// <summary>
    /// Performs the action associated with the current DSP command.
    /// </summary>
    private bool ProcessCommand() {
        _outputData.Clear();
        switch (_currentCommand) {
            case Commands.GetVersionNumber:
                _outputData.Enqueue(4);
                _outputData.Enqueue(5);
                break;

            case Commands.DspIdentification:
                _outputData.Enqueue((byte)~_commandData[0]);
                break;

            case Commands.SetTimeConstant:
                _dsp.SampleRate = 256000000 / (65536 - (_commandData[0] << 8));
                break;

            case Commands.SetSampleRate:
                _dsp.SampleRate = _commandData[0] << 8 | _commandData[1];
                break;

            case Commands.SetBlockTransferSize:
                _dsp.BlockTransferSize = (_commandData[0] | _commandData[1] << 8) + 1;
                _blockTransferSizeSet = true;
                break;

            case Commands.SingleCycleDmaOutput8:
            case Commands.HighSpeedSingleCycleDmaOutput8:
            case Commands.SingleCycleDmaOutput8_Alt:
            case Commands.SingleCycleDmaOutput8Fifo_Alt:
                _dsp.Begin(false, false, false);
                _machine.DmaSubsystem.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM4Ref:
                _dsp.Begin(false, false, false, CompressionLevel.ADPCM4, true);
                _machine.DmaSubsystem.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM4:
                _dsp.Begin(false, false, false, CompressionLevel.ADPCM4, false);
                _machine.DmaSubsystem.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM2Ref:
                _dsp.Begin(false, false, false, CompressionLevel.ADPCM2, true);
                _machine.DmaSubsystem.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM2:
                _dsp.Begin(false, false, false, CompressionLevel.ADPCM2, false);
                _machine.DmaSubsystem.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM3Ref:
                _dsp.Begin(false, false, false, CompressionLevel.ADPCM3, true);
                _machine.DmaSubsystem.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM3:
                _dsp.Begin(false, false, false, CompressionLevel.ADPCM3, false);
                _machine.DmaSubsystem.PerformDmaTransfers();
                break;

            case Commands.AutoInitDmaOutput8:
            case Commands.HighSpeedAutoInitDmaOutput8:
                if (!_blockTransferSizeSet) {
                    _dsp.BlockTransferSize = (_commandData[1] | _commandData[2] << 8) + 1;
                }

                _dsp.Begin(false, false, true);
                break;

            case Commands.AutoInitDmaOutput8_Alt:
            case Commands.AutoInitDmaOutput8Fifo_Alt:
                if (!_blockTransferSizeSet) {
                    _dsp.BlockTransferSize = (_commandData[1] | _commandData[2] << 8) + 1;
                }

                _dsp.Begin(false, (_commandData[0] & 1 << 5) != 0, true);
                break;

            case Commands.ExitAutoInit8:
                _dsp.ExitAutoInit();
                break;

            case Commands.SingleCycleDmaOutput16:
            case Commands.SingleCycleDmaOutput16Fifo:
                _dsp.Begin(true, (_commandData[0] & 1 << 5) != 0, false);
                break;

            case Commands.AutoInitDmaOutput16:
            case Commands.AutoInitDmaOutput16Fifo:
                _dsp.Begin(true, (_commandData[0] & 1 << 5) != 0, true);
                break;

            case Commands.TurnOnSpeaker:
                break;

            case Commands.TurnOffSpeaker:
                break;

            case Commands.PauseDmaMode:
            case Commands.PauseDmaMode16:
            case Commands.ExitDmaMode16:
                _dmaChannel.IsActive = false;
                _dsp.IsEnabled = false;
                break;

            case Commands.ContinueDmaMode:
            case Commands.ContinueDmaMode16:
                _dmaChannel.IsActive = true;
                _dsp.IsEnabled = true;
                break;

            case Commands.RaiseIrq8:
                RaiseInterruptRequest();
                break;

            case Commands.SetInputSampleRate:
                // Ignore for now.
                break;

            case Commands.PauseForDuration:
                _pauseDuration = _commandData[0] | _commandData[1] << 8;
                break;

            default:
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("Sound Blaster command {CurrentCommand} not implemented", _currentCommand);
                }

                return false;
        }
        _state = BlasterState.WaitingForCommand;
        return true;
    }

    /// <inheritdoc/>
    public void RaiseInterruptRequest() {
        _mixer.InterruptStatusRegister = InterruptStatus.Dma8;
        _machine.DualPic.ProcessInterruptRequest(IRQ);
    }

    /// <summary>
    /// Resets the DSP.
    /// </summary>
    private void Reset() {
        _outputData.Clear();
        _outputData.Enqueue(0xAA);
        _state = BlasterState.WaitingForCommand;
        _blockTransferSizeSet = false;
        _dsp.Reset();
    }
}