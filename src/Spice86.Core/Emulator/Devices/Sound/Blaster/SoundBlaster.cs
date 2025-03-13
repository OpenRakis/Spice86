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

/// <summary>
/// Sound blaster implementation. <br/>
/// http://www.fysnet.net/detectsb.htm
/// </summary>
public class SoundBlaster : DefaultIOPortHandler, IDmaDevice8, IDmaDevice16, IRequestInterrupt,
    IBlasterEnvVarProvider, IDisposable {

    /// <summary>
    /// The port number for the MPU-401 MIDI data port.
    /// </summary>
    public const int MPU401_DATA_PORT = 0x300;

    /// <summary>
    /// The port number for the MPU-401 MIDI status/command port.
    /// </summary>
    public const int MPU401_STATUS_COMMAND_PORT = 0x301;


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
    /// An undocumented port that is ignored by the 'Hardware Programming Guide' and only logged by DOSBox Staging's Sound Blaster implementation.
    /// </summary>
    public const int IGNORE_PORT = 0x0227;

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
    
    private const int ResampleRate = 48000;

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
    private readonly DeviceThread _deviceThread;
    private bool _blockTransferSizeSet;
    private byte _commandDataLength;
    private byte _currentCommand;
    private int _pauseDuration;
    private BlasterState _blasterState;
    private readonly DualPic _dualPic;
    private readonly byte[] _readFromDspBuffer = new byte[512];
    private readonly short[] _renderingBuffer = new short[65536 * 2];
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
    public SbType SbType { get; set; } = SbType.SbPro;

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
        _dualPic = dualPic;
        _eightByteDmaChannel = dmaController.Channels[soundBlasterHardwareConfig.LowDma];
        _dsp = new Dsp(_eightByteDmaChannel, dmaController.Channels[soundBlasterHardwareConfig.HighDma]);
        _dsp.OnAutoInitBufferComplete += RaiseInterruptRequest;
        dmaController.SetupDmaDeviceChannel(this);
        _deviceThread = new DeviceThread(nameof(SoundBlaster), PlaybackLoopBody, pauseHandler, loggerService);
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
    public override byte ReadByte(ushort port) {
        switch (port) {
            case MPU401_DATA_PORT:
                return 0x0;
            case MPU401_STATUS_COMMAND_PORT:
                return 0xC0; //No data, and the interface is not ready
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
            case DspPorts.DspReset:
                return 0xFF;
            default:
                return base.ReadByte(port);
        }
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        _deviceThread.StartThreadIfNeeded();
        switch (port) {
            case MPU401_DATA_PORT:
                //ignored
                return;
            case MPU401_STATUS_COMMAND_PORT:
                //ignored
                return;
            case DspPorts.DspWriteStatus:
                //ignored
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
            case IGNORE_PORT:
                if(_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("Sound Blaster ignored port write {PortNumber:X2} with value {alue:X2}", 
                        port, value);
                }
                break;
            default:
                base.WriteByte(port, value);
                break;
        }
    }

    /// <inheritdoc />
    public override ushort ReadWord(ushort port) {
        uint value = ReadByte(port);
        value |= (uint)(ReadByte((ushort)(port + 1)) << 8);
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
        DspPorts.DspReadData, DspPorts.DspWrite, DspPorts.DspReadBufferStatus, DspPorts.MixerAddress, DspPorts.MixerData, DspPorts.DspReset,
        IGNORE_PORT
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
                _deviceThread.Dispose();
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
        ioPortDispatcher.AddIOPortHandler(IGNORE_PORT, this);
        ioPortDispatcher.AddIOPortHandler(MPU401_DATA_PORT, this);
        ioPortDispatcher.AddIOPortHandler(MPU401_STATUS_COMMAND_PORT, this);
        // Those are managed by OPL3FM class.
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

    private void PlaybackLoopBody() {
        _dsp.Read(_readFromDspBuffer);
        int length = Resample(_readFromDspBuffer, ResampleRate, _renderingBuffer);
        PCMSoundChannel.Render(_renderingBuffer.AsSpan(0, length));

        if (_pauseDuration > 0) {
            Array.Clear(_renderingBuffer, 0, _renderingBuffer.Length);
            int count = (_pauseDuration / (1024 / 2)) + 1;
            for (int i = 0; i < count; i++) {
                PCMSoundChannel.Render(_renderingBuffer.AsSpan(0, 1024));
            }

            _pauseDuration = 0;
            RaiseInterruptRequest();
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