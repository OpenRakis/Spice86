namespace Spice86.Emulator.Devices.Sound;

using Serilog;

using Spice86.Emulator.IOPorts;
using Spice86.Emulator.Memory;
using Spice86.Emulator.Sound;
using Spice86.Emulator.Sound.Blaster;
using Spice86.Emulator.VM;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

/// <summary>
/// Sound blaster implementation. <br/>
/// http://www.fysnet.net/detectsb.htm
/// </summary>
public class SoundBlaster : DefaultIOPortHandler, IDmaDevice8, IDmaDevice16, IDisposable {

    private ILogger _logger = Log.Logger.ForContext<SoundBlaster>();

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

    private static readonly SortedList<byte, byte> commandLengths = new SortedList<byte, byte> {
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

    private readonly List<byte> commandData = new();
    private readonly int dma16;
    private readonly DmaChannel dmaChannel;
    private readonly Dsp dsp;
    private readonly Mixer mixer;
    private readonly Queue<byte> outputData = new();
    private readonly Thread playbackThread;
    private readonly Machine vm;
    private bool blockTransferSizeSet;
    private byte commandDataLength;
    private byte currentCommand;
    private volatile bool endPlayback;
    private int pauseDuration;
    private volatile bool pausePlayback;
    private BlasterState state;

    /// <summary>
    /// Initializes a new instance of the SoundBlaster class.
    /// </summary>
    /// <param name="vm">Virtual machine instance associated with the device.</param>
    /// <param name="hwnd">Main application window handle.</param>
    /// <param name="irq">IRQ number for the Sound Blaster.</param>
    /// <param name="dma8">8-bit DMA channel for the Sound Blaster.</param>
    /// <param name="dma16">16-bit DMA channel for the Sound Blaster.</param>
    public SoundBlaster(Machine vm, Configuration configuration, int irq = 7, int dma8 = 1, int dma16 = 5) : base(vm, configuration) {
        this.vm = vm ?? throw new ArgumentNullException(nameof(vm));
        this.vm.Paused += Vm_Paused;
        this.vm.Resumed += Vm_Resumed;
        this.IRQ = irq;
        this.DMA = dma8;
        this.dma16 = dma16;
        this.mixer = new Mixer(this);
        this.dmaChannel = vm.DmaController.Channels[this.DMA];
        this.dsp = new Dsp(vm, dma8, dma16);
        this.dsp.AutoInitBufferComplete += (o, e) => RaiseInterrupt();
        this.playbackThread = new Thread(this.AudioPlayback) {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        this.playbackThread.Start();
    }

    private void Vm_Resumed(object? sender, EventArgs e) {
        this.pausePlayback = false;
    }

    private void Vm_Paused(object? sender, EventArgs e) {
        this.pausePlayback = true;
    }

    public override byte ReadByte(int port) {
        switch (port) {
            case Ports.DspReadData:
                if (outputData.Count > 0)
                    return this.outputData.Dequeue();
                else
                    return 0;

            case Ports.DspWrite:
                return 0x00;

            case Ports.DspReadBufferStatus:
                if (this.mixer.InterruptStatusRegister == InterruptStatus.Dma8)
                    _logger.Information("Sound Blaster 8-bit DMA acknowledged");
                this.mixer.InterruptStatusRegister = InterruptStatus.None;
                return this.outputData.Count > 0 ? (byte)0x80 : (byte)0u;

            case Ports.MixerAddress:
                return (byte)this.mixer.CurrentAddress;

            case Ports.MixerData:
                return this.mixer.ReadData();
        }

        return 0;
    }

    public override void WriteByte(int port, byte value) {
        switch (port) {
            case Ports.DspReset:
                // Expect a 1, then 0 written to reset the DSP.
                if (value == 1)
                    this.state = BlasterState.ResetRequest;
                else if (value == 0 && this.state == BlasterState.ResetRequest) {
                    this.state = BlasterState.Resetting;
                    Reset();
                }
                break;

            case Ports.DspWrite:
                if (this.state == BlasterState.WaitingForCommand) {
                    this.currentCommand = value;
                    this.state = BlasterState.ReadingCommand;
                    this.commandData.Clear();
                    commandLengths.TryGetValue(value, out this.commandDataLength);
                    if (this.commandDataLength == 0)
                        ProcessCommand();
                } else if (this.state == BlasterState.ReadingCommand) {
                    this.commandData.Add(value);
                    if (this.commandData.Count >= this.commandDataLength)
                        ProcessCommand();
                }
                break;

            case Ports.MixerAddress:
                this.mixer.CurrentAddress = value;
                break;
        }
    }

    public override ushort ReadWord(int port) {
        uint value = ReadByte(port);
        value |= (uint)(ReadByte(port + 1) << 8);
        return (ushort)value;
    }

    public override void WriteWord(int port, ushort value) {
        WriteWord(port, value);
    }

    int IDmaDevice8.Channel => this.DMA;

    int IDmaDevice16.Channel => this.dma16;

    /// <summary>
    /// Gets the DMA channel assigned to the device.
    /// </summary>
    public int DMA { get; }

    public IEnumerable<int> InputPorts => new int[] { Ports.DspReadData, Ports.DspWrite, Ports.DspReadBufferStatus, Ports.MixerAddress, Ports.MixerData };

    /// <summary>
    /// Gets the hardware IRQ assigned to the device.
    /// </summary>
    public int IRQ { get; }

    public IEnumerable<int> OutputPorts => new int[] { Ports.DspReset, Ports.DspWrite, Ports.MixerAddress };

    public void Dispose() {
        if (this.playbackThread != null && this.playbackThread.IsAlive) {
            this.endPlayback = true;
            this.playbackThread.Join();
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
        ioPortDispatcher.AddIOPortHandler(FM_MUSIC_STATUS_PORT_NUMBER_2, this);
        ioPortDispatcher.AddIOPortHandler(FM_MUSIC_DATA_PORT_NUMBER, this);
        ioPortDispatcher.AddIOPortHandler(FM_MUSIC_DATA_PORT_NUMBER_2, this);
    }

    public void Pause() {
        this.pausePlayback = true;
    }

    public void Resume() {
        this.pausePlayback = false;
    }

    void IDmaDevice8.SingleCycleComplete() {
        this.dsp.IsEnabled = false;
        RaiseInterrupt();
    }

    void IDmaDevice16.SingleCycleComplete() => throw new NotImplementedException();

    int IDmaDevice8.WriteBytes(ReadOnlySpan<byte> source) => this.dsp.DmaWrite(source);

    int IDmaDevice16.WriteWords(IntPtr source, int count) => throw new NotImplementedException();

    internal void AddEnvironnmentVariable() {
        _machine.EnvironmentVariables["BLASTER"] = $"A220 I{this.IRQ} D{this.DMA} T4";
    }

    private void AudioPlayback() {
        Span<byte> buffer = stackalloc byte[512];
        short[] writeBuffer = new short[65536 * 2];

        using TinyAudio.AudioPlayer? player = Audio.CreatePlayer();
        int sampleRate = (int)player.Format.SampleRate;
        player.BeginPlayback();

        while (!this.endPlayback) {
            this.dsp.Read(buffer);
            int length;
            if (this.dsp.Is16Bit && this.dsp.IsStereo)
                length = LinearUpsampler.Resample16Stereo(dsp.SampleRate, sampleRate, MemoryMarshal.Cast<byte, short>(buffer), writeBuffer);
            else if (this.dsp.Is16Bit)
                length = LinearUpsampler.Resample16Mono(dsp.SampleRate, sampleRate, MemoryMarshal.Cast<byte, short>(buffer), writeBuffer);
            else if (this.dsp.IsStereo)
                length = LinearUpsampler.Resample8Stereo(dsp.SampleRate, sampleRate, buffer, writeBuffer);
            else
                length = LinearUpsampler.Resample8Mono(dsp.SampleRate, sampleRate, buffer, writeBuffer);

            Audio.WriteFullBuffer(player, writeBuffer.AsSpan(0, length));

            if (this.pausePlayback) {
                player.StopPlayback();
                while (this.pausePlayback) {
                    Thread.Sleep(1);
                    if (this.endPlayback)
                        return;
                }

                player.BeginPlayback();
            }

            if (this.pauseDuration > 0) {
                Array.Clear(writeBuffer, 0, writeBuffer.Length);
                int count = this.pauseDuration / (1024 / 2) + 1;
                for (int i = 0; i < count; i++)
                    Audio.WriteFullBuffer(player, writeBuffer.AsSpan(0, 1024));

                this.pauseDuration = 0;
                RaiseInterrupt();
            }
        }
    }

    /// <summary>
    /// Performs the action associated with the current DSP command.
    /// </summary>
    private void ProcessCommand() {
        outputData.Clear();
        switch (currentCommand) {
            case Commands.GetVersionNumber:
                outputData.Enqueue(4);
                outputData.Enqueue(5);
                break;

            case Commands.DspIdentification:
                outputData.Enqueue((byte)~commandData[0]);
                break;

            case Commands.SetTimeConstant:
                dsp.SampleRate = 256000000 / (65536 - (commandData[0] << 8));
                break;

            case Commands.SetSampleRate:
                dsp.SampleRate = (commandData[0] << 8) | commandData[1];
                break;

            case Commands.SetBlockTransferSize:
                dsp.BlockTransferSize = (commandData[0] | (commandData[1] << 8)) + 1;
                this.blockTransferSizeSet = true;
                break;

            case Commands.SingleCycleDmaOutput8:
            case Commands.HighSpeedSingleCycleDmaOutput8:
            case Commands.SingleCycleDmaOutput8_Alt:
            case Commands.SingleCycleDmaOutput8Fifo_Alt:
                //if(commandData.Count >= 2 && (commandData[0] | (commandData[1] << 8)) >= 2048)
                dsp.Begin(false, false, false);
                //else
                //    this.vm.InterruptController.RaiseHardwareInterrupt(this.irq);
                _logger.Information("Single-cycle DMA");
                vm.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM4Ref:
                dsp.Begin(false, false, false, CompressionLevel.ADPCM4, true);
                _logger.Information("Single-cycle DMA ADPCM4 with reference byte");
                vm.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM4:
                dsp.Begin(false, false, false, CompressionLevel.ADPCM4, false);
                _logger.Information("Single-cycle DMA ADPCM4");
                vm.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM2Ref:
                dsp.Begin(false, false, false, CompressionLevel.ADPCM2, true);
                _logger.Information("Single-cycle DMA ADPCM2 with reference byte");
                vm.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM2:
                dsp.Begin(false, false, false, CompressionLevel.ADPCM2, false);
                _logger.Information("Single-cycle DMA ADPCM2");
                vm.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM3Ref:
                dsp.Begin(false, false, false, CompressionLevel.ADPCM3, true);
                _logger.Information("Single-cycle DMA ADPCM3 with reference byte");
                vm.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM3:
                dsp.Begin(false, false, false, CompressionLevel.ADPCM3, false);
                _logger.Information("Single-cycle DMA ADPCM3");
                vm.PerformDmaTransfers();
                break;

            case Commands.AutoInitDmaOutput8:
            case Commands.HighSpeedAutoInitDmaOutput8:
                if (!this.blockTransferSizeSet)
                    dsp.BlockTransferSize = ((commandData[1] | (commandData[2] << 8)) + 1);
                this.dsp.Begin(false, false, true);
                _logger.Information("Auto-init DMA");
                break;

            case Commands.AutoInitDmaOutput8_Alt:
            case Commands.AutoInitDmaOutput8Fifo_Alt:
                if (!this.blockTransferSizeSet)
                    dsp.BlockTransferSize = ((commandData[1] | (commandData[2] << 8)) + 1);
                this.dsp.Begin(false, (commandData[0] & (1 << 5)) != 0, true);
                _logger.Information("Auto-init DMA");
                break;

            case Commands.ExitAutoInit8:
                this.dsp.ExitAutoInit();
                break;

            case Commands.SingleCycleDmaOutput16:
            case Commands.SingleCycleDmaOutput16Fifo:
                this.dsp.Begin(true, (commandData[0] & (1 << 5)) != 0, false);
                _logger.Information("Single-cycle DMA");
                break;

            case Commands.AutoInitDmaOutput16:
            case Commands.AutoInitDmaOutput16Fifo:
                _logger.Information("Auto-init DMA");
                this.dsp.Begin(true, (commandData[0] & (1 << 5)) != 0, true);
                break;

            case Commands.TurnOnSpeaker:
                break;

            case Commands.TurnOffSpeaker:
                break;

            case Commands.PauseDmaMode:
            case Commands.PauseDmaMode16:
            case Commands.ExitDmaMode16:
                this.dmaChannel.IsActive = false;
                this.dsp.IsEnabled = false;
                _logger.Information("Pause Sound Blaster DMA");
                break;

            case Commands.ContinueDmaMode:
            case Commands.ContinueDmaMode16:
                this.dmaChannel.IsActive = true;
                this.dsp.IsEnabled = true;
                _logger.Information("Continue Sound Blaster DMA");
                break;

            case Commands.RaiseIrq8:
                RaiseInterrupt();
                break;

            case Commands.SetInputSampleRate:
                // Ignore for now.
                break;

            case Commands.PauseForDuration:
                this.pauseDuration = commandData[0] | (commandData[1] << 8);
                break;

            default:
                throw new NotImplementedException($"Sound Blaster command {currentCommand:X2}h not implemented.");
        }

        this.state = BlasterState.WaitingForCommand;
    }

    /// <summary>
    /// Raises a hardware interrupt and prepares for an acknowledge response.
    /// </summary>
    private void RaiseInterrupt() {
        this.mixer.InterruptStatusRegister = InterruptStatus.Dma8;
        this.vm.Pic.ProcessInterrupt((byte)this.IRQ);
        _logger.Information("Sound Blaster IRQ");
    }

    /// <summary>
    /// Resets the DSP.
    /// </summary>
    private void Reset() {
        outputData.Clear();
        outputData.Enqueue(0xAA);
        this.state = BlasterState.WaitingForCommand;
        this.blockTransferSizeSet = false;
        this.dsp.Reset();
    }
}