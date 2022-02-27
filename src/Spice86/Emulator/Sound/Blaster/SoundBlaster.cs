namespace Spice86.Emulator.Sound.Blaster;

using Spice86.Emulator.Devices;
using Spice86.Emulator.Memory;
using Spice86.Emulator.VM;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Emulates a Sound Blaster 16 device.
/// </summary>
public sealed partial class SoundBlaster : IInputPort, IOutputPort, IDmaDevice8, IDmaDevice16, IDisposable {
    private readonly Machine vm;
    private readonly DmaChannel dmaChannel;
    private readonly List<byte> commandData = new();
    private readonly Queue<byte> outputData = new();
    private readonly int dma16;
    private readonly Thread playbackThread;
    private readonly Dsp dsp;
    private readonly Mixer mixer;
    private volatile bool pausePlayback;
    private volatile bool endPlayback;
    private byte currentCommand;
    private byte commandDataLength;
    private BlasterState state;
    private bool blockTransferSizeSet;
    private int pauseDuration;

    /// <summary>
    /// Initializes a new instance of the SoundBlaster class.
    /// </summary>
    /// <param name="vm">Virtual machine instance associated with the device.</param>
    /// <param name="hwnd">Main application window handle.</param>
    /// <param name="irq">IRQ number for the Sound Blaster.</param>
    /// <param name="dma8">8-bit DMA channel for the Sound Blaster.</param>
    /// <param name="dma16">16-bit DMA channel for the Sound Blaster.</param>
    public SoundBlaster(Machine vm, int irq, int dma8, int dma16) {
        this.vm = vm ?? throw new ArgumentNullException(nameof(vm));
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
    /// <summary>
    /// Initializes a new instance of the SoundBlaster class.
    /// </summary>
    /// <param name="vm">Virtual machine instance associated with the device.</param>
    /// <param name="hwnd">Main application window handle.</param>
    public SoundBlaster(Machine vm)
        : this(vm, 7, 1, 5) {
    }

    /// <summary>
    /// Gets the hardware IRQ assigned to the device.
    /// </summary>
    public int IRQ { get; }
    /// <summary>
    /// Gets the DMA channel assigned to the device.
    /// </summary>
    public int DMA { get; }

    IEnumerable<int> IInputPort.InputPorts => new int[] { Ports.DspReadData, Ports.DspWrite, Ports.DspReadBufferStatus, Ports.MixerAddress, Ports.MixerData };
    byte IInputPort.ReadByte(int port) {
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
                    System.Diagnostics.Debug.WriteLine("Sound Blaster 8-bit DMA acknowledged");
                this.mixer.InterruptStatusRegister = InterruptStatus.None;
                return this.outputData.Count > 0 ? (byte)0x80 : (byte)0u;

            case Ports.MixerAddress:
                return (byte)this.mixer.CurrentAddress;

            case Ports.MixerData:
                return this.mixer.ReadData();
        }

        return 0;
    }
    ushort IInputPort.ReadWord(int port) {
        uint value = ((IInputPort)this).ReadByte(port);
        value |= (uint)(((IInputPort)this).ReadByte(port + 1) << 8);
        return (ushort)value;
    }

    IEnumerable<int> IOutputPort.OutputPorts => new int[] { Ports.DspReset, Ports.DspWrite, Ports.MixerAddress };
    void IOutputPort.WriteByte(int port, byte value) {
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
    void IOutputPort.WriteWord(int port, ushort value) => throw new NotImplementedException();

    int IDmaDevice8.Channel => this.DMA;
    int IDmaDevice8.WriteBytes(ReadOnlySpan<byte> source) => this.dsp.DmaWrite(source);
    void IDmaDevice8.SingleCycleComplete() {
        this.dsp.IsEnabled = false;
        RaiseInterrupt();
    }

    int IDmaDevice16.Channel => this.dma16;
    int IDmaDevice16.WriteWords(IntPtr source, int count) => throw new NotImplementedException();
    void IDmaDevice16.SingleCycleComplete() => throw new NotImplementedException();

    void IVirtualDevice.Pause() {
        this.pausePlayback = true;
    }
    void IVirtualDevice.Resume() {
        this.pausePlayback = false;
    }
    void IVirtualDevice.DeviceRegistered(Machine vm) {
        vm.EnvironmentVariables["BLASTER"] = $"A220 I{this.IRQ} D{this.DMA} T4";
    }

    public void Dispose() {
        if (this.playbackThread != null && this.playbackThread.IsAlive) {
            this.endPlayback = true;
            this.playbackThread.Join();
        }
    }

    /// <summary>
    /// Raises a hardware interrupt and prepares for an acknowledge response.
    /// </summary>
    private void RaiseInterrupt() {
        this.mixer.InterruptStatusRegister = InterruptStatus.Dma8;
        this.vm.Pic.ProcessInterrupt((byte)this.IRQ);
        System.Diagnostics.Debug.WriteLine("Sound Blaster IRQ");
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
                System.Diagnostics.Debug.WriteLine("Single-cycle DMA");
                vm.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM4Ref:
                dsp.Begin(false, false, false, CompressionLevel.ADPCM4, true);
                System.Diagnostics.Debug.WriteLine("Single-cycle DMA ADPCM4 with reference byte");
                vm.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM4:
                dsp.Begin(false, false, false, CompressionLevel.ADPCM4, false);
                System.Diagnostics.Debug.WriteLine("Single-cycle DMA ADPCM4");
                vm.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM2Ref:
                dsp.Begin(false, false, false, CompressionLevel.ADPCM2, true);
                System.Diagnostics.Debug.WriteLine("Single-cycle DMA ADPCM2 with reference byte");
                vm.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM2:
                dsp.Begin(false, false, false, CompressionLevel.ADPCM2, false);
                System.Diagnostics.Debug.WriteLine("Single-cycle DMA ADPCM2");
                vm.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM3Ref:
                dsp.Begin(false, false, false, CompressionLevel.ADPCM3, true);
                System.Diagnostics.Debug.WriteLine("Single-cycle DMA ADPCM3 with reference byte");
                vm.PerformDmaTransfers();
                break;

            case Commands.SingleCycleDmaOutputADPCM3:
                dsp.Begin(false, false, false, CompressionLevel.ADPCM3, false);
                System.Diagnostics.Debug.WriteLine("Single-cycle DMA ADPCM3");
                vm.PerformDmaTransfers();
                break;

            case Commands.AutoInitDmaOutput8:
            case Commands.HighSpeedAutoInitDmaOutput8:
                if (!this.blockTransferSizeSet)
                    dsp.BlockTransferSize = ((commandData[1] | (commandData[2] << 8)) + 1);
                this.dsp.Begin(false, false, true);
                System.Diagnostics.Debug.WriteLine("Auto-init DMA");
                break;

            case Commands.AutoInitDmaOutput8_Alt:
            case Commands.AutoInitDmaOutput8Fifo_Alt:
                if (!this.blockTransferSizeSet)
                    dsp.BlockTransferSize = ((commandData[1] | (commandData[2] << 8)) + 1);
                this.dsp.Begin(false, (commandData[0] & (1 << 5)) != 0, true);
                System.Diagnostics.Debug.WriteLine("Auto-init DMA");
                break;

            case Commands.ExitAutoInit8:
                this.dsp.ExitAutoInit();
                break;

            case Commands.SingleCycleDmaOutput16:
            case Commands.SingleCycleDmaOutput16Fifo:
                this.dsp.Begin(true, (commandData[0] & (1 << 5)) != 0, false);
                System.Diagnostics.Debug.WriteLine("Single-cycle DMA");
                break;

            case Commands.AutoInitDmaOutput16:
            case Commands.AutoInitDmaOutput16Fifo:
                System.Diagnostics.Debug.WriteLine("Auto-init DMA");
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
                System.Diagnostics.Debug.WriteLine("Pause Sound Blaster DMA");
                break;

            case Commands.ContinueDmaMode:
            case Commands.ContinueDmaMode16:
                this.dmaChannel.IsActive = true;
                this.dsp.IsEnabled = true;
                System.Diagnostics.Debug.WriteLine("Continue Sound Blaster DMA");
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
}

