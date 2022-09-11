namespace Spice86.Core.Emulator.Devices.Sound;

using Serilog;

using Spice86.Core.Backend.Audio;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Sound;
using Spice86.Core.Emulator.Sound.Blaster;
using Spice86.Core.Emulator.VM;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

/// <summary>
/// Sound blaster implementation. <br/>
/// http://www.fysnet.net/detectsb.htm
/// </summary>
public sealed class SoundBlaster : DefaultIOPortHandler, IDmaDevice8, IDmaDevice16, IDisposable {
    private const int DSP_DATA_AVAILABLE_PORT_NUMBER = 0x22E;
    private const int DSP_READ_PORT_NUMBER = 0x22A;
    private const int DSP_RESET_PORT_NUMBER = 0x226;
    private const int DSP_WRITE_BUFFER_STATUS_PORT_NUMBER = 0x22C;
    private const int FM_MUSIC_DATA_PORT_NUMBER = 0x229;
    private const int FM_MUSIC_DATA_PORT_NUMBER_2 = 0x389;
    private const int FM_MUSIC_STATUS_PORT_NUMBER = 0x228;
    private const int FM_MUSIC_STATUS_PORT_NUMBER_2 = 0x388;
    private const int LEFT_SPEAKER_DATA_PORT_NUMBER = 0x221;
    private const int LEFT_SPEAKER_STATUS_PORT_NUMBER = 0x220;
    private const int MIXER_DATA_PORT_NUMBER = 0x225;
    private const int MIXER_REGISTER_PORT_NUMBER = 0x224;
    private const int RIGHT_SPEAKER_DATA_PORT_NUMBER = 0x223;
    private const int RIGHT_SPEAKER_STATUS_PORT_NUMBER = 0x222;

    private bool _disposed = false;

    private static readonly SortedList<byte, byte> commandLengths = new() {
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
    private volatile bool _pausePlayback;
    private BlasterState _state;
    private bool _playbackStarted = false;

    /// <summary>
    /// Initializes a new instance of the SoundBlaster class.
    /// </summary>
    /// <param name="machine">Virtual machine instance associated with the device.</param>
    /// <param name="irq">IRQ number for the Sound Blaster.</param>
    /// <param name="dma8">8-bit DMA channel for the Sound Blaster.</param>
    /// <param name="dma16">16-bit DMA channel for the Sound Blaster.</param>
    public SoundBlaster(Machine machine, Configuration configuration, byte irq = 7, int dma8 = 1, int dma16 = 5) : base(machine, configuration) {
        _machine.Paused += MachinePaused;
        _machine.Resumed += MachineResumed;
        IRQ = irq;
        DMA = dma8;
        _dma16 = dma16;
        _mixer = new Mixer(this);
        _dmaChannel = machine.DmaController.Channels[DMA];
        _dsp = new Dsp(machine, dma8, dma16);
        _dsp.AutoInitBufferComplete += (o, e) => RaiseInterrupt();
        _playbackThread = new Thread(AudioPlayback) {
            Name = "PCMAudio",
            Priority = ThreadPriority.AboveNormal
        };
    }

    private void MachineResumed() {
        _pausePlayback = false;
    }

    private void MachinePaused() {
        _pausePlayback = true;
    }

    public override byte ReadByte(int port) {
        switch (port) {
            case Ports.DspReadData:
                if (_outputData.Count > 0) {
                    return _outputData.Dequeue();
                } else {
                    return 0;
                }

            case Ports.DspWrite:
                return 0x00;

            case Ports.DspReadBufferStatus:
                if (_mixer.InterruptStatusRegister == InterruptStatus.Dma8) {
                    //System.Diagnostics.Debug.WriteLine("Sound Blaster 8-bit DMA acknowledged");
                }

                _mixer.InterruptStatusRegister = InterruptStatus.None;
                return _outputData.Count > 0 ? (byte)0x80 : (byte)0u;

            case Ports.MixerAddress:
                return (byte)_mixer.CurrentAddress;

            case Ports.MixerData:
                return _mixer.ReadData();
        }
        return 0;
    }

    public override void WriteByte(int port, byte value) {
        if (!_playbackStarted) {
            _playbackThread.Start();
            _playbackStarted = true;
        }
        switch (port) {
            case Ports.DspReset:
                // Expect a 1, then 0 written to reset the DSP.
                if (value == 1) {
                    _state = BlasterState.ResetRequest;
                } else if (value == 0 && _state == BlasterState.ResetRequest) {
                    _state = BlasterState.Resetting;
                    Reset();
                }
                break;

            case Ports.DspWrite:
                if (_state == BlasterState.WaitingForCommand) {
                    _currentCommand = value;
                    _state = BlasterState.ReadingCommand;
                    _commandData.Clear();
                    commandLengths.TryGetValue(value, out _commandDataLength);
                    if (_commandDataLength == 0) {
                        ProcessCommand();
                    }
                } else if (_state == BlasterState.ReadingCommand) {
                    _commandData.Add(value);
                    if (_commandData.Count >= _commandDataLength) {
                        ProcessCommand();
                    }
                }
                break;

            case Ports.MixerAddress:
                _mixer.CurrentAddress = value;
                break;
        }
    }

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

    public IEnumerable<int> InputPorts => new int[] { Ports.DspReadData, Ports.DspWrite, Ports.DspReadBufferStatus, Ports.MixerAddress, Ports.MixerData };

    /// <summary>
    /// Gets the hardware IRQ assigned to the device.
    /// </summary>
    public byte IRQ { get; }

    public IEnumerable<int> OutputPorts => new int[] { Ports.DspReset, Ports.DspWrite, Ports.MixerAddress };

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

    public void Pause() {
        _pausePlayback = true;
    }

    public void Resume() {
        _pausePlayback = false;
    }

    void IDmaDevice8.SingleCycleComplete() {
        _dsp.IsEnabled = false;
        RaiseInterrupt();
    }

    void IDmaDevice16.SingleCycleComplete() => throw new NotImplementedException();

    int IDmaDevice8.WriteBytes(ReadOnlySpan<byte> source) => _dsp.DmaWrite(source);

    int IDmaDevice16.WriteWords(IntPtr source, int count) => throw new NotImplementedException();

    internal void AddEnvironnmentVariable() {
        _machine.EnvironmentVariables["BLASTER"] = $"A220 I{IRQ} D{DMA} T4";
    }

    private void AudioPlayback() {
        if (!Configuration.CreateAudioBackend) {
            return;
        }
        using AudioPlayer? player = Audio.CreatePlayer(48000, 2048);
        if (player is null) {
            return;
        }
        Span<byte> buffer = stackalloc byte[512];
        short[] writeBuffer = new short[65536 * 2];
        int sampleRate = player.Format.SampleRate;
        player.BeginPlayback();

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

            if (_pausePlayback) {
                player.StopPlayback();
                while (_pausePlayback) {
                    Thread.Sleep(1);
                    if (_endPlayback) {
                        return;
                    }
                }
                player.BeginPlayback();
            }

            if (_pauseDuration > 0) {
                Array.Clear(writeBuffer, 0, writeBuffer.Length);
                int count = _pauseDuration / (1024 / 2) + 1;
                for (int i = 0; i < count; i++) {
                    Audio.WriteFullBuffer(player, writeBuffer.AsSpan(0, 1024));
                }

                _pauseDuration = 0;
                RaiseInterrupt();
            }
        }
    }

    /// <summary>
    /// Performs the action associated with the current DSP command.
    /// </summary>
    private void ProcessCommand() {
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
                //if(commandData.Count >= 2 && (commandData[0] | (commandData[1] << 8)) >= 2048)
                _dsp.Begin(false, false, false);
                //else
                //    vm.InterruptController.RaiseHardwareInterrupt(irq);
                //System.Diagnostics.Debug.WriteLine("Single-cycle DMA");
                _machine.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM4Ref:
                _dsp.Begin(false, false, false, CompressionLevel.ADPCM4, true);
                //System.Diagnostics.Debug.WriteLine("Single-cycle DMA ADPCM4 with reference byte");
                _machine.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM4:
                _dsp.Begin(false, false, false, CompressionLevel.ADPCM4, false);
                //System.Diagnostics.Debug.WriteLine("Single-cycle DMA ADPCM4");
                _machine.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM2Ref:
                _dsp.Begin(false, false, false, CompressionLevel.ADPCM2, true);
                //System.Diagnostics.Debug.WriteLine("Single-cycle DMA ADPCM2 with reference byte");
                _machine.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM2:
                _dsp.Begin(false, false, false, CompressionLevel.ADPCM2, false);
                //System.Diagnostics.Debug.WriteLine("Single-cycle DMA ADPCM2");
                _machine.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM3Ref:
                _dsp.Begin(false, false, false, CompressionLevel.ADPCM3, true);
                //System.Diagnostics.Debug.WriteLine("Single-cycle DMA ADPCM3 with reference byte");
                _machine.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM3:
                _dsp.Begin(false, false, false, CompressionLevel.ADPCM3, false);
                //System.Diagnostics.Debug.WriteLine("Single-cycle DMA ADPCM3");
                _machine.PerformDmaTransfers();
                break;

            case Commands.AutoInitDmaOutput8:
            case Commands.HighSpeedAutoInitDmaOutput8:
                if (!_blockTransferSizeSet) {
                    _dsp.BlockTransferSize = (_commandData[1] | _commandData[2] << 8) + 1;
                }

                _dsp.Begin(false, false, true);
                //System.Diagnostics.Debug.WriteLine("Auto-init DMA");
                break;

            case Commands.AutoInitDmaOutput8_Alt:
            case Commands.AutoInitDmaOutput8Fifo_Alt:
                if (!_blockTransferSizeSet) {
                    _dsp.BlockTransferSize = (_commandData[1] | _commandData[2] << 8) + 1;
                }

                _dsp.Begin(false, (_commandData[0] & 1 << 5) != 0, true);
                //System.Diagnostics.Debug.WriteLine("Auto-init DMA");
                break;

            case Commands.ExitAutoInit8:
                _dsp.ExitAutoInit();
                break;

            case Commands.SingleCycleDmaOutput16:
            case Commands.SingleCycleDmaOutput16Fifo:
                _dsp.Begin(true, (_commandData[0] & 1 << 5) != 0, false);
                //System.Diagnostics.Debug.WriteLine("Single-cycle DMA");
                break;

            case Commands.AutoInitDmaOutput16:
            case Commands.AutoInitDmaOutput16Fifo:
                //System.Diagnostics.Debug.WriteLine("Auto-init DMA");
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
                //System.Diagnostics.Debug.WriteLine("Pause Sound Blaster DMA");
                break;

            case Commands.ContinueDmaMode:
            case Commands.ContinueDmaMode16:
                _dmaChannel.IsActive = true;
                _dsp.IsEnabled = true;
                //System.Diagnostics.Debug.WriteLine("Continue Sound Blaster DMA");
                break;

            case Commands.RaiseIrq8:
                RaiseInterrupt();
                break;

            case Commands.SetInputSampleRate:
                // Ignore for now.
                break;

            case Commands.PauseForDuration:
                _pauseDuration = _commandData[0] | _commandData[1] << 8;
                break;

            default:
                throw new NotImplementedException($"Sound Blaster command {_currentCommand:X2}h not implemented.");
        }

        _state = BlasterState.WaitingForCommand;
    }

    /// <summary>
    /// Raises a hardware interrupt and prepares for an acknowledge response.
    /// </summary>
    private void RaiseInterrupt() {
        _mixer.InterruptStatusRegister = InterruptStatus.Dma8;
        _machine.DualPic.ProcessInterruptRequest(IRQ);
        //System.Diagnostics.Debug.WriteLine("Sound Blaster IRQ");
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