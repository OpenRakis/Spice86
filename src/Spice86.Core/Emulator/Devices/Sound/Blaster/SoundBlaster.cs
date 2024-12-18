namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Sound.Ymf262Emu;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Frozen;
using System.Threading;

/// <summary>
/// Sound blaster implementation. <br/>
/// http://www.fysnet.net/detectsb.htm
/// </summary>
public class SoundBlaster : DefaultIOPortHandler, IDmaDevice8, IDmaDevice16, IRequestInterrupt,
    IBlasterEnvVarProvider, IDisposable {
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
    /// The port used to set the DSP status.
    /// </summary>
    public const int DSP_WRITE_STATUS = 0x0C;

    /// <summary>
    /// The port used to get the DSP status.
    /// </summary>
    public const int DSP_READ_STATUS = 0x0E;

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

    private static readonly FrozenDictionary<byte, byte> CommandLengths = new Dictionary<byte, byte>() {
        { Commands.SetTimeConstant, 1 },
        { Commands.SingleCycleDmaOutput8, 2 },
        { Commands.DspIdentification, 1 },
        { Commands.SetBlockTransferSize, 2 },
        { Commands.SetSampleRate, 2 },
        { Commands.SetInputSampleRate, 2 },
        { Commands.SingleCycleDmaOutput16, 3 },
        { Commands.AutoInitDmaOutput16, 3 },
        { Commands.SingleCycleDmaOutput16Fifo, 3 },
        { Commands.AutoInitDmaOutput16Fifo, 3 },
        { Commands.SingleCycleDmaOutput8_Alt, 3 },
        { Commands.AutoInitDmaOutput8_Alt, 3 },
        { Commands.SingleCycleDmaOutput8Fifo_Alt, 3 },
        { Commands.AutoInitDmaOutput8Fifo_Alt, 3 },
        { Commands.PauseForDuration, 2 },
        { Commands.SingleCycleDmaOutputADPCM4Ref, 2 },
        { Commands.SingleCycleDmaOutputADPCM2Ref, 2 },
        { Commands.SingleCycleDmaOutputADPCM3Ref, 2 }
    }.ToFrozenDictionary();

    private readonly List<byte> _commandData = new();
    private readonly int _dma16;
    private readonly DmaChannel _eightByteDmaChannel;
    private readonly Dsp _dsp;
    private readonly HardwareMixer _ctMixer;
    private readonly Queue<byte> _outputData = new();
    private readonly Thread _playbackThread;
    private bool _blockTransferSizeSet;
    private byte _commandDataLength;
    private byte _currentCommand;
    private volatile bool _endPlayback;
    private int _pauseDuration;
    private BlasterState _blasterState;
    private bool _playbackStarted;
    private readonly DualPic _dualPic;
    private readonly IPauseHandler _pauseHandler;

    /// <summary>
    /// The SoundBlaster's PCM sound channel.
    /// </summary>
    public SoundChannel PCMSoundChannel { get; }

    /// <summary>
    /// The SoundBlaster's OPL3 FM sound channel.
    /// </summary>
    public SoundChannel FMSynthSoundChannel { get; }
    
    /// <summary>
    /// The internal FM synth chip for music.
    /// </summary>
    public OPL3FM Opl3Fm { get; }

    /// <summary>
    /// The type of SoundBlaster card currently emulated.
    /// </summary>
    public SbType SbType { get; set; } = SbType.Sb16;

    /// <summary>
    /// Initializes a new instance of the SoundBlaster class.
    /// </summary>
    /// <param name="ioPortDispatcher">The class that is responsible for dispatching ports reads and writes to classes that respond to them.</param>
    /// <param name="softwareMixer">The emulator's sound mixer.</param>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="dmaController">The DMA controller used for PCM data transfers by the DSP.</param>
    /// <param name="dualPic">The two programmable interrupt controllers.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an IO port wasn't handled.</param>
    /// <param name="loggerService">The logging service used for logging events.</param>
    /// <param name="soundBlasterHardwareConfig">The IRQ, low DMA, and high DMA configuration.</param>
    /// <param name="pauseHandler">The handler for the emulation pause state.</param>
    public SoundBlaster(IOPortDispatcher ioPortDispatcher, SoftwareMixer softwareMixer, State state, DmaController dmaController,
        DualPic dualPic, bool failOnUnhandledPort, ILoggerService loggerService,
        SoundBlasterHardwareConfig soundBlasterHardwareConfig, IPauseHandler pauseHandler) : base(state, failOnUnhandledPort, loggerService) {
        SbType = soundBlasterHardwareConfig.SbType;
        IRQ = soundBlasterHardwareConfig.Irq;
        DMA = soundBlasterHardwareConfig.LowDma;
        _dma16 = soundBlasterHardwareConfig.HighDma;
        _pauseHandler = pauseHandler;
        _dualPic = dualPic;
        _eightByteDmaChannel = dmaController.Channels[soundBlasterHardwareConfig.LowDma];
        _dsp = new Dsp(_eightByteDmaChannel, dmaController.Channels[soundBlasterHardwareConfig.HighDma]);
        _dsp.OnAutoInitBufferComplete += RaiseInterruptRequest;
        dmaController.SetupDmaDeviceChannel(this);
        _playbackThread = new Thread(AudioPlayback) {
            Name = nameof(SoundBlaster),
        };
        PCMSoundChannel = softwareMixer.CreateChannel(nameof(SoundBlaster));
        FMSynthSoundChannel = softwareMixer.CreateChannel(nameof(OPL3FM));
        Opl3Fm = new OPL3FM(FMSynthSoundChannel, state, ioPortDispatcher, failOnUnhandledPort, loggerService, pauseHandler);
        _ctMixer = new HardwareMixer(soundBlasterHardwareConfig, PCMSoundChannel, FMSynthSoundChannel, loggerService);
        InitPortHandlers(ioPortDispatcher);
    }

    /// <summary>
    /// The BLASTER environment variable
    /// </summary>
    public string BlasterString => $"A220 I{IRQ} D{DMA} T4";

    /// <inheritdoc />
    public override byte ReadByte(int port) {
        switch (port) {
            case DspPorts.DspReadStatus:
                return _dsp.IsDmaTransferActive ? (byte)0xff : (byte)0x7f;
            case DspPorts.DspReadData:
                return _outputData.Count > 0 ? _outputData.Dequeue() : (byte)0;
            case DspPorts.DspWrite:
                return 0x00;
            case DspPorts.DspReadBufferStatus:
                _ctMixer.InterruptStatusRegister = InterruptStatus.None;
                return _outputData.Count > 0 ? (byte)0x80 : (byte)0u;
            case DspPorts.MixerAddress:
                return (byte)_ctMixer.CurrentAddress;
            case DspPorts.MixerData:
                return _ctMixer.ReadData();
            default:
                return base.ReadByte(port);
        }
    }

    /// <inheritdoc />
    public override void WriteByte(int port, byte value) {
        if (!_playbackStarted) {
            _loggerService.Information("Starting thread '{ThreadName}'", _playbackThread.Name ?? nameof(SoundBlaster));
            _playbackStarted = true;
            _playbackThread.Start();
        }

        switch (port) {
            case DspPorts.DspWriteStatus:
                return;
            case DspPorts.DspReset:
                switch (value) {
                    // Expect a 1, then 0 written to reset the DSP.
                    case 1:
                        _blasterState = BlasterState.ResetRequest;
                        break;
                    case 0 when _blasterState == BlasterState.ResetRequest: {
                        _blasterState = BlasterState.Resetting;
                        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                            _loggerService.Verbose("SoundBlaster DSP was reset");
                        }
                        Reset();
                        break;
                    }
                }
                break;
            case DspPorts.DspWrite:
                switch (_blasterState) {
                    case BlasterState.WaitingForCommand: {
                        _currentCommand = value;
                        _blasterState = BlasterState.ReadingCommand;
                        _commandData.Clear();
                        CommandLengths.TryGetValue(value, out _commandDataLength);
                        if (_commandDataLength == 0) {
                            if (!ProcessCommand()) {
                                base.WriteByte(port, value);
                            }
                        }
                        break;
                    }
                    case BlasterState.ReadingCommand: {
                        _commandData.Add(value);
                        if (_commandData.Count >= _commandDataLength) {
                            if (!ProcessCommand()) {
                                base.WriteByte(port, value);
                            }
                        }
                        break;
                    }
                }
                break;
            case DspPorts.MixerData:
                _ctMixer.Write(value);
                break;
            case DspPorts.MixerAddress:
                _ctMixer.CurrentAddress = value;
                break;
            default:
                base.WriteByte(port, value);
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
    public FrozenSet<int> InputPorts => new int[] {
        DspPorts.DspReadData, DspPorts.DspWrite, DspPorts.DspReadBufferStatus, DspPorts.MixerAddress, DspPorts.MixerData
    }.ToFrozenSet();

    /// <summary>
    /// Gets the hardware IRQ assigned to the device.
    /// </summary>
    public byte IRQ { get; }

    /// <summary>
    /// The list of output ports.
    /// </summary>
    public FrozenSet<int> OutputPorts =>
        new int[] { DspPorts.DspReset, DspPorts.DspWrite, DspPorts.MixerAddress }.ToFrozenSet();

    /// <inheritdoc />
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _endPlayback = true;
                if (_playbackThread.IsAlive) {
                    _playbackThread.Join();
                }

                _dsp.Dispose();
            }

            _disposed = true;
        }
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(DSP_RESET_PORT_NUMBER, this);
        ioPortDispatcher.AddIOPortHandler(DSP_READ_STATUS, this);
        ioPortDispatcher.AddIOPortHandler(DSP_WRITE_STATUS, this);
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
        // Those are managed by OPL class.
        //ioPortDispatcher.AddIOPortHandler(FM_MUSIC_STATUS_PORT_NUMBER_2, this);
        //ioPortDispatcher.AddIOPortHandler(FM_MUSIC_DATA_PORT_NUMBER_2, this);
    }

    void IDmaDevice8.SingleCycleComplete() {
        _dsp.IsDmaTransferActive = false;
        RaiseInterruptRequest();
    }

    void IDmaDevice16.SingleCycleComplete() => throw new NotImplementedException();

    int IDmaDevice8.WriteBytes(ReadOnlySpan<byte> source) => _dsp.DmaWrite(source);

    int IDmaDevice16.WriteWords(IntPtr source, int count) => throw new NotImplementedException();

    private void AudioPlayback() {
        Span<byte> buffer = stackalloc byte[512];
        short[] writeBuffer = new short[65536 * 2];
        const int sampleRate = 48000;

        while (!_endPlayback) {
            _pauseHandler.WaitIfPaused();
            _dsp.Read(buffer);
            int length = Resample(buffer, sampleRate, writeBuffer);
            PCMSoundChannel.Render(writeBuffer.AsSpan(0, length));

            if (_pauseDuration > 0) {
                Array.Clear(writeBuffer, 0, writeBuffer.Length);
                int count = (_pauseDuration / (1024 / 2)) + 1;
                for (int i = 0; i < count; i++) {
                    PCMSoundChannel.Render(writeBuffer.AsSpan(0, 1024));
                }

                _pauseDuration = 0;
                RaiseInterruptRequest();
            }
        }
    }

    /// <summary>
    /// Resamples the data in sourceBuffer to destinationBuffer with the given sampleRate. Returns the destinationBuffer length.
    /// </summary>
    /// <param name="sourceBuffer"></param>
    /// <param name="sampleRate"></param>
    /// <param name="destinationBuffer"></param>
    /// <returns>Length of the data written in destinationBuffer</returns>
    private int Resample(Span<byte> sourceBuffer, int sampleRate, short[] destinationBuffer) {
        if (_dsp.Is16Bit && _dsp.IsStereo) {
            return LinearUpsampler.Resample16Stereo(_dsp.SampleRate, sampleRate, sourceBuffer.Cast<byte, short>(),
                destinationBuffer);
        }

        if (_dsp.Is16Bit) {
            return LinearUpsampler.Resample16Mono(_dsp.SampleRate, sampleRate, sourceBuffer.Cast<byte, short>(),
                destinationBuffer);
        }

        if (_dsp.IsStereo) {
            return LinearUpsampler.Resample8Stereo(_dsp.SampleRate, sampleRate, sourceBuffer, destinationBuffer);
        }

        return LinearUpsampler.Resample8Mono(_dsp.SampleRate, sampleRate, sourceBuffer, destinationBuffer);
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
                break;

            case Commands.SingleCycleDmaOutputADPCM4Ref:
                _dsp.Begin(false, false, false, CompressionLevel.ADPCM4, true);
                break;

            case Commands.SingleCycleDmaOutputADPCM4:
                _dsp.Begin(false, false, false, CompressionLevel.ADPCM4, false);
                break;

            case Commands.SingleCycleDmaOutputADPCM2Ref:
                _dsp.Begin(false, false, false, CompressionLevel.ADPCM2, true);
                break;

            case Commands.SingleCycleDmaOutputADPCM2:
                _dsp.Begin(false, false, false, CompressionLevel.ADPCM2, false);
                break;

            case Commands.SingleCycleDmaOutputADPCM3Ref:
                _dsp.Begin(false, false, false, CompressionLevel.ADPCM3, true);
                break;

            case Commands.SingleCycleDmaOutputADPCM3:
                _dsp.Begin(false, false, false, CompressionLevel.ADPCM3, false);
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
                _eightByteDmaChannel.IsActive = false;
                _dsp.IsDmaTransferActive = false;
                break;

            case Commands.ContinueDmaMode:
            case Commands.ContinueDmaMode16:
                _eightByteDmaChannel.IsActive = true;
                _dsp.IsDmaTransferActive = true;
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

        _blasterState = BlasterState.WaitingForCommand;
        return true;
    }

    /// <inheritdoc/>
    public void RaiseInterruptRequest() {
        _ctMixer.InterruptStatusRegister = InterruptStatus.Dma8;
        _dualPic.ProcessInterruptRequest(IRQ);
    }

    /// <summary>
    /// Resets the DSP.
    /// </summary>
    private void Reset() {
        _outputData.Clear();
        _outputData.Enqueue(0xAA);
        _blasterState = BlasterState.WaitingForCommand;
        _blockTransferSizeSet = false;
        _dsp.Reset();
    }
}