namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Audio.Backend;
using Spice86.Audio.Common;
using Spice86.Audio.Filters;
using Spice86.Core.CLI.RuntimeOptions;
using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.DeviceScheduler;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;

/// <summary>
/// Gravis UltraSound (GF1 chip) emulation.
/// Supports 14-32 independent PCM voices with independent frequency, panning,
/// volume-ramp envelopes, and 8/16-bit sample playback from on-board DRAM.
/// </summary>
/// <remarks>
/// Port map (at default base 0x240, portBase = gusBase - 0x200 = 0x40):
/// 0x200+pb - mix control (w); 0x206+pb - IRQ status (r); 0x208+pb - timer status (r/w);
/// 0x209+pb - timer control (w); 0x20A+pb - AdLib command mirror (r);
/// 0x20B+pb - IRQ/DMA select (w);
/// 0x302+pb - voice index (r/w); 0x303+pb - GF1 register select (r/w);
/// 0x304+pb - data word (r/w); 0x305+pb - data high byte (r/w);
/// 0x307+pb - DRAM byte (r/w).
///
/// Emulated GF1 features: 14-32 software-selectable PCM voices; 8/16-bit samples with
/// linear interpolation; forward and bidirectional looping; per-voice volume-ramp
/// envelopes (4 rate banks); 16-position constant-power stereo panning; wave, volume,
/// timer and DMA IRQs with voice-IRQ auto-advance; two hardware timers (80 us and 320 us
/// base periods); DMA upload and recording with terminal-count IRQ; 1 MiB on-board DRAM
/// with direct peek/poke access; runtime IRQ/DMA reassignment via port 0x20B.
///
/// Command-line configuration: GusEnable (master switch), GusBase, GusIrq, GusDma,
/// GusUltradir and GusFilter (low-pass output filter, on by default).
///
/// ULTRADIR / patch workflow: the emulator exports the ULTRASND and ULTRADIR environment
/// variables to DOS so that the real Gravis drivers (ULTRAMID.EXE, ULTRASND.COM) installed
/// under the mounted DOS drive can locate their instrument patches (ULTRADIR\MIDI\*.PAT)
/// and upload them into DRAM via DMA. The emulator deliberately does not parse .PAT files
/// itself; patch loading is performed by the DOS-side drivers, matching dosbox-staging.
///
/// Known limitations: the GUS MIDI UART is not emulated (the IRQ2/DMA2 selections made via
/// port 0x20B are accepted but have no effect); AMD InterWave features are not emulated.
///
/// Reference implementation: dosbox-staging gus.cpp / gus.h
/// (c) 2022-2025 The DOSBox Staging Team
/// </remarks>
public sealed class GravisUltraSound : DefaultIOPortHandler, IRequestInterrupt, IAudioQueueDevice<AudioFrame>, IMixerQueueNotifier {

    // Public constants used by GusVoice

    /// <summary>Number of pan-position slots (0-15).</summary>
    public const byte PanPositions = 16;

    /// <summary>Default (centre) pan-position index.</summary>
    public const byte PanDefaultPosition = 7;

    /// <summary>Number of distinct volume-scalar entries in the lookup table.</summary>
    public const int VolLevels = 4096;

    /// <summary>Fixed-point scale applied to volume-ramp position values.</summary>
    public const int VolumeIncScalar = 512;

    /// <summary>Size of the emulated on-board GUS DRAM in bytes (1 MiB).</summary>
    public const int DramSizeBytes = 1024 * 1024;

    // Hardware constants

    private const int MaxVoices = 32;
    private const int MinVoices = 14;
    private const int GusOutputSampleRateHz = 44100;
    private const int DmaTransferSizeBytes = 8 * 1024;
    private const int IsaBusThroughputBytesPerSecond = 32 * 1024 * 1024;
    private const double DmaTransferDelayMs = 1000.0 / (IsaBusThroughputBytesPerSecond / DmaTransferSizeBytes);

    // GF1 timer base delays in milliseconds (80 µs and 320 µs)
    private const double Timer1DefaultDelayMs = 0.080;
    private const double Timer2DefaultDelayMs = 0.320;

    // Volume scaling constant: 0.0235 dB per increment step
    private const double DeltaDb = 0.002709201;

    // Default AdLib command register value
    private const byte AdlibCmdDefault = 85;

    // Default mix control register state: latches enabled, line-in and line-out disabled
    private const byte MixControlDefault = 0x0B;

    // IRQ status byte bits
    private const byte IrqWaveStateBit = (byte)GusIrqStatus.WaveTable;
    private const byte IrqVolStateBit = (byte)GusIrqStatus.VolumeRamp;
    private const byte IrqDmaFinished = (byte)GusIrqStatus.DmaTerminalCount;
    private const byte IrqTimer1Bit = (byte)GusIrqStatus.Timer1;
    private const byte IrqTimer2Bit = (byte)GusIrqStatus.Timer2;

    // Mix control register bits
    private const byte MixCtrlLatchesEnabled = 0x08;
    private const byte MixCtrlIrqCtrlSelected = 0x40;

    // Reset register bits
    private const byte ResetRegIsRunning = 0x01;
    private const byte ResetRegDacEnabled = 0x02;
    private const byte ResetRegIrqsEnabled = 0x04;

    // DMA control register bits
    private const byte DmaCtrlEnabled = (byte)GusDmaControl.Enabled;
    private const byte DmaCtrlGusToHost = (byte)GusDmaControl.GusToHost;
    private const byte DmaCtrlChannel16Bit = (byte)GusDmaControl.Channel16Bit;
    private const byte DmaCtrlWantsIrqOnTc = (byte)GusDmaControl.RaiseIrqOnTerminalCount;
    private const byte DmaCtrlSamples16Bit = (byte)GusDmaControl.Samples16Bit; // write: samples are 16-bit
    private const byte DmaCtrlTcIrqPending = (byte)GusDmaControl.Samples16Bit; // read: TC IRQ pending
    private const byte DmaCtrlInvertHighBit = (byte)GusDmaControl.InvertHighBit;

    // IRQ address lookup table (index → IRQ number), per GUS SDK section 2.14
    private static readonly byte[] IrqAddresses = { 0, 2, 5, 3, 7, 11, 12, 15 };

    // DMA address lookup table (index → DMA channel), per GUS SDK section 2.15
    private static readonly byte[] DmaAddresses = { 0, 1, 3, 5, 6, 7 };

    // Fields

    private readonly byte[] _ram = new byte[DramSizeBytes];

    private readonly GusVoice[] _voices;
    private readonly GusVoiceIrq _voiceIrq = new GusVoiceIrq();
    private readonly GusTimer[] _timers = new GusTimer[2];

    private readonly float[] _volScalars = new float[VolLevels];
    private readonly AudioFrame[] _panScalars = new AudioFrame[PanPositions];

    private readonly DmaBus _dmaBus;
    private readonly DualPic _dualPic;
    private readonly SoftwareMixer _mixer;
    private readonly DeviceScheduler _scheduler;
    private readonly IEmulatedClock _clock;
    private readonly Spice86.Core.Emulator.VM.DeviceScheduler.EventHandler[] _timerEventHandlers;
    private readonly Spice86.Core.Emulator.VM.DeviceScheduler.EventHandler _dmaEventHandler;
    private readonly Spice86.Core.Emulator.VM.DeviceScheduler.EventHandler _tickEventHandler;
    private readonly SoundChannel _channel;
    private readonly RWQueue<AudioFrame> _outputQueue = new RWQueue<AudioFrame>(4096);

    private readonly string _ultraDir;
    private readonly ushort _gusBase;

    // Not readonly: these can be reassigned at runtime via port 0x20B.
    private byte _irq;
    private byte _irq2;
    private byte _dma;
    private byte _dma2;

    // GF1 register / I/O state
    private byte _voiceIndex;
    private byte _selectedReg;
    private int _dramAddr;
    private byte _dmaControlReg;
    private bool _dmaSamples16Bit;
    private byte _irqStatus;
    private ushort _registerData;
    private byte _resetReg;
    private byte _mixControl = MixControlDefault;
    private byte _timerCtrl;
    private byte _sampleCtrl;
    private byte _adlibCommandReg = AdlibCmdDefault;
    private ushort _dmaAddr;
    private byte _dmaAddressNibble;
    private bool _shouldChangeIrqDma;
    private bool _irqPreviouslyInterrupted;

    private int _activeVoices = MinVoices;
    private uint _activeVoiceMask = 0xFFFFFFFFu >> (MaxVoices - MinVoices);
    private GusVoice? _targetVoice;
    private float _frameCounter;
    private double _lastRenderedMs;
    private double _millisecondsPerFrame;

    // Render scratch buffer (grows on demand, never shrinks)
    private AudioFrame[] _renderBuf;

    // Constructor

    /// <summary>
    /// Initialises the GUS emulation and registers all hardware I/O ports.
    /// </summary>
    public GravisUltraSound(
        State state,
        IOPortDispatcher ioPortDispatcher,
        bool failOnUnhandledPort,
        ILogger loggerService,
        DmaBus dmaBus,
        DualPic dualPic,
        SoftwareMixer mixer,
        DeviceScheduler scheduler,
        IEmulatedClock clock,
        AudioRuntimeOptions audioOptions)
        : base(state, failOnUnhandledPort, loggerService) {

        _dmaBus = dmaBus;
        _dualPic = dualPic;
        _mixer = mixer;
        _scheduler = scheduler;
        _clock = clock;
        _irq = ToInternalIrq(audioOptions.GusIrq);
        _irq2 = _irq;
        _dma = audioOptions.GusDma;
        _dma2 = _dma;
        _gusBase = audioOptions.GusBase;
        _ultraDir = audioOptions.GusUltradir;

        int portBase = _gusBase - 0x200;

        BuildVolScalars();
        BuildPanScalars();

        _voices = new GusVoice[MaxVoices];
        for (byte v = 0; v < MaxVoices; v++) {
            _voices[v] = new GusVoice(v, _voiceIrq);
        }

        _timers[0] = new GusTimer(Timer1DefaultDelayMs);
        _timers[1] = new GusTimer(Timer2DefaultDelayMs);
        _timerEventHandlers = [
            _ => OnTimerExpired(0),
            _ => OnTimerExpired(1)
        ];
        _dmaEventHandler = _ => ProcessDmaTransfer();
        _tickEventHandler = _ => OnSchedulerTick();

        InitPortHandlers(ioPortDispatcher, portBase);

        // Mixer setup
        mixer.RegisterQueueNotifier(this);
        mixer.LockMixerThread();

        HashSet<ChannelFeature> features = new HashSet<ChannelFeature> {
            ChannelFeature.DigitalAudio,
            ChannelFeature.Stereo,
            ChannelFeature.Sleep,
            ChannelFeature.ReverbSend,
            ChannelFeature.ChorusSend
        };
        _channel = mixer.AddChannel(MixerCallback, GetSampleRate(), "GravisUltraSound", features);
        _channel.SetZeroOrderHoldUpsamplerTargetRate(GusOutputSampleRateHz);
        _channel.SetResampleMethod(ResampleMethod.ZeroOrderHoldAndResample);
        _millisecondsPerFrame = 1000.0 / GetSampleRate();
        _lastRenderedMs = _clock.ElapsedTimeMs;
        _scheduler.AddEvent(_tickEventHandler, 1, 0);

        int queueCapacity = (int)Math.Ceiling(_channel.FramesPerBlock * 2.0f);
        _outputQueue.Resize(queueCapacity);
        _renderBuf = new AudioFrame[Math.Max(1, (int)_channel.FramesPerBlock)];

        if (audioOptions.GusFilter) {
            // First-order low-pass at 8 kHz, emulating the analog output stage of the real
            // card (dosbox-staging gus_filter = on).
            _channel.ConfigureLowPassFilter(1, 8000);
            _channel.LowPassFilter = FilterState.On;
        }

        mixer.UnlockMixerThread();

        _dualPic.SetIrqMask(_irq, false);

        DmaChannel? dmaChannel = _dmaBus.GetChannel(_dma);
        dmaChannel?.ReserveFor("GravisUltraSound", OnDmaChannelEvicted);
        dmaChannel?.RegisterCallback(OnDmaEvent);
    }

    // Interface implementations

    /// <inheritdoc/>
    public RWQueue<AudioFrame> OutputQueue => _outputQueue;

    /// <inheritdoc/>
    public SoundChannel Channel => _channel;

    /// <inheritdoc/>
    public void NotifyLockMixer() => _outputQueue.Stop();

    /// <inheritdoc/>
    public void NotifyUnlockMixer() => _outputQueue.Start();

    /// <inheritdoc/>
    public void RaiseInterruptRequest() {
        bool shouldInterrupt = (_irqStatus & (IsIrqsEnabled ? 0xFF : 0x9F)) != 0;
        if (shouldInterrupt && IsLatchesEnabled) {
            _dualPic.ActivateIrq(_irq);
        } else if (_irqPreviouslyInterrupted) {
            _dualPic.DeactivateIrq(_irq);
        }
        _irqPreviouslyInterrupted = shouldInterrupt;
    }

    // Moves the IRQ line at runtime (port 0x20B selection): drops any pending request on
    // the old line, masks it, then unmasks the new line and re-evaluates pending IRQs.
    private void ChangeIrq(byte newIrq) {
        if (newIrq == _irq) {
            return;
        }
        _dualPic.DeactivateIrq(_irq);
        _dualPic.SetIrqMask(_irq, true);
        _irq = newIrq;
        _irqPreviouslyInterrupted = false;
        _dualPic.SetIrqMask(_irq, false);
        RaiseInterruptRequest();
    }

    /// <summary>
    /// Returns the ULTRASND environment variable value, in the standard
    /// "base,dma1,dma2,irq1,irq2" format (for example "240,3,3,5,5").
    /// </summary>
    public string UltraSndString =>
        $"{_gusBase:X3},{_dma},{_dma2},{ToExternalIrq(_irq)},{ToExternalIrq(_irq2)}";

    /// <summary>
    /// Returns the ULTRADIR environment variable value: the DOS-side directory holding the
    /// Gravis drivers and instrument patches, set via the GusUltradir configuration option.
    /// </summary>
    public string UltraDirString => _ultraDir;

    /// <summary>
    /// Mirrors an AdLib command-port write into the GUS command register.
    /// </summary>
    /// <param name="value">The byte written to AdLib command port 0x388.</param>
    /// <remarks>
    /// The classic GUS exposes the last command written to the AdLib command port through
    /// GUS port 0x20A. DOSBox Staging forwards this value from the OPL device.
    /// </remarks>
    public void MirrorAdlibCommandRegister(byte value) {
        _adlibCommandReg = value;
    }

    /// <summary>
    /// The 32 GF1 voices, in voice-number order. Intended for machine-code override
    /// implementers that need to inspect or drive voice state directly.
    /// </summary>
    public IReadOnlyList<GusVoice> Voices => _voices;

    /// <summary>
    /// Number of voices currently enabled for mixing (14-32), as last programmed through
    /// GF1 register 0x0E. The output sample rate scales down as voices are added.
    /// </summary>
    public int ActiveVoices => _activeVoices;

    /// <summary>True when the GF1 is out of reset (reset register 0x4C, bit 0).</summary>
    public bool IsRunning => (_resetReg & ResetRegIsRunning) != 0;

    /// <summary>True when the DAC output is enabled (reset register 0x4C, bit 1).</summary>
    public bool IsDacEnabled => (_resetReg & ResetRegDacEnabled) != 0;

    /// <summary>True when IRQ generation is enabled (reset register 0x4C, bit 2).</summary>
    private bool IsIrqsEnabled => (_resetReg & ResetRegIrqsEnabled) != 0;

    /// <summary>True when the IRQ/DMA latches are enabled (mix control register, bit 3).</summary>
    private bool IsLatchesEnabled => (_mixControl & MixCtrlLatchesEnabled) != 0;

    /// <summary>Configured GUS base I/O port (0x240 by default).</summary>
    public ushort BasePort => _gusBase;

    /// <summary>Currently selected playback IRQ line, as seen by DOS software (IRQ 9 is reported as 2).</summary>
    public byte PlaybackIrq => ToExternalIrq(_irq);

    /// <summary>Currently selected recording IRQ line, as seen by DOS software.</summary>
    public byte RecordingIrq => ToExternalIrq(_irq2);

    /// <summary>Currently selected playback DMA channel.</summary>
    public byte PlaybackDma => _dma;

    /// <summary>Currently selected recording DMA channel.</summary>
    public byte RecordingDma => _dma2;

    /// <summary>Reset register 0x4C: bit 0 running, bit 1 DAC enabled, bit 2 IRQs enabled.</summary>
    public byte ResetRegister => _resetReg;

    /// <summary>Mix control register (port 0x240).</summary>
    public byte MixControlRegister => _mixControl;

    /// <summary>Timer control register 0x45.</summary>
    public byte TimerControlRegister => _timerCtrl;

    /// <summary>DMA sampling control register 0x49.</summary>
    public byte SampleControlRegister => _sampleCtrl;

    /// <summary>DMA control register 0x41.</summary>
    public byte DmaControlRegister => _dmaControlReg;

    /// <summary>Pending IRQ status bits. Unlike the port 0x246 read, this does not clear them.</summary>
    public byte IrqStatusRegister => _irqStatus;

    /// <summary>Timer/AdLib status bits, as reported by a port 0x248 read.</summary>
    public byte TimerStatusRegister => GetTimerStatus();

    /// <summary>Last value mirrored from the AdLib command port 0x388.</summary>
    public byte AdlibCommandRegister => _adlibCommandReg;

    /// <summary>GF1 register currently selected through port 0x343.</summary>
    public byte SelectedRegister => _selectedReg;

    /// <summary>Data word latched for the currently selected GF1 register.</summary>
    public ushort SelectedRegisterData => _registerData;

    /// <summary>Voice currently selected through port 0x342.</summary>
    public byte SelectedVoiceIndex => _voiceIndex;

    /// <summary>DMA DRAM address register 0x42.</summary>
    public ushort DmaAddressRegister => _dmaAddr;

    /// <summary>Sub-paragraph nibble of the current DMA DRAM address.</summary>
    public byte DmaAddressNibble => _dmaAddressNibble;

    /// <summary>True when the DMA control register declares the transferred samples as 16-bit.</summary>
    public bool AreDmaSamples16Bit => _dmaSamples16Bit;

    /// <summary>True when the transfer itself uses a 16-bit DMA channel.</summary>
    public bool IsDmaTransfer16Bit => IsDmaXfer16Bit();

    /// <summary>Byte offset in DRAM the next DMA transfer starts from.</summary>
    public uint DmaDramOffset => GetDmaOffset();

    /// <summary>DRAM address used by the peek/poke port 0x347, set through registers 0x43/0x44.</summary>
    public int DramAddress => _dramAddr;

    /// <summary>Bitmask of the voices currently taking part in mixing.</summary>
    public uint ActiveVoiceMask => _activeVoiceMask;

    /// <summary>True when IRQ generation is enabled (reset register 0x4C, bit 2).</summary>
    public bool AreIrqsEnabled => IsIrqsEnabled;

    /// <summary>True when the IRQ/DMA latches are enabled (mix control register, bit 3).</summary>
    public bool AreLatchesEnabled => IsLatchesEnabled;

    /// <summary>Current output sample rate in Hz, which scales down as voices are added.</summary>
    public int SampleRate => GetSampleRate();

    /// <summary>Snapshot of the IRQ bitmasks shared by all voices.</summary>
    public GusVoiceIrqState VoiceIrqState =>
        new(_voiceIrq.VolState, _voiceIrq.WaveState, _voiceIrq.Status);

    /// <summary>Number of GF1 hardware timers.</summary>
    public int TimerCount => _timers.Length;

    /// <summary>Returns a snapshot of the given hardware timer.</summary>
    /// <param name="index">Timer index, 0 (80 us base period) or 1 (320 us base period).</param>
    public GusTimerState GetTimerState(int index) {
        if (index < 0 || index >= _timers.Length) {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        GusTimer timer = _timers[index];
        return new GusTimerState(timer.Delay, timer.Value, timer.HasExpired,
            timer.IsCountingDown, timer.IsMasked, timer.ShouldRaiseIrq);
    }

    /// <summary>
    /// Copies a contiguous block of on-board DRAM into <paramref name="destination"/>, wrapping at 1 MiB.
    /// </summary>
    /// <param name="address">DRAM byte address to start reading from; only the low 20 bits are used.</param>
    /// <param name="destination">Buffer receiving the bytes; its length determines how many are read.</param>
    public void ReadDram(int address, Span<byte> destination) {
        for (int i = 0; i < destination.Length; i++) {
            destination[i] = _ram[(address + i) & 0xFFFFF];
        }
    }

    /// <summary>
    /// Reads a byte from the emulated on-board DRAM.
    /// The address wraps at the 1 MiB boundary, like the real hardware.
    /// </summary>
    /// <param name="address">DRAM byte address; only the low 20 bits are used.</param>
    /// <returns>The byte stored at the wrapped address.</returns>
    public byte PeekDramByte(int address) => _ram[address & 0xFFFFF];

    /// <summary>
    /// Writes a byte to the emulated on-board DRAM.
    /// The address wraps at the 1 MiB boundary, like the real hardware.
    /// </summary>
    /// <param name="address">DRAM byte address; only the low 20 bits are used.</param>
    /// <param name="value">The byte to store.</param>
    public void PokeDramByte(int address, byte value) => _ram[address & 0xFFFFF] = value;

    /// <summary>
    /// Reads a little-endian 16-bit word from the emulated on-board DRAM, wrapping at 1 MiB.
    /// </summary>
    /// <param name="address">DRAM byte address; only the low 20 bits are used.</param>
    /// <returns>The word stored at the wrapped address.</returns>
    public ushort PeekDramWord(int address) {
        int i = address & 0xFFFFF;
        return (ushort)(_ram[i] | (_ram[(i + 1) & 0xFFFFF] << 8));
    }

    /// <summary>
    /// Writes a little-endian 16-bit word to the emulated on-board DRAM, wrapping at 1 MiB.
    /// </summary>
    /// <param name="address">DRAM byte address; only the low 20 bits are used.</param>
    /// <param name="value">The word to store.</param>
    public void PokeDramWord(int address, ushort value) {
        int i = address & 0xFFFFF;
        _ram[i] = (byte)value;
        _ram[(i + 1) & 0xFFFFF] = (byte)(value >> 8);
    }

    // I/O port overrides

    /// <inheritdoc/>
    public override byte ReadByte(ushort port) {
        RenderUpToNow();

        int offset = port - _gusBase;
        switch (offset) {
            case 0x06:  // IRQ status
                return GetIrqStatus();
            case 0x08:  // Timer/AdLib status
                return GetTimerStatus();
            case 0x0A:  // AdLib command mirror
                return _adlibCommandReg;
            default:
                break;
        }

        int highOffset = port - (_gusBase + 0x100);
        switch (highOffset) {
            case 0x02: // voice index
                return _voiceIndex;
            case 0x03: // GF1 register select
                return _selectedReg;
            case 0x04: // data low byte
                return (byte)ReadFromRegister();
            case 0x05: // data high byte
                return (byte)(ReadFromRegister() >> 8);
            case 0x07: // DRAM byte
                return _dramAddr < DramSizeBytes ? _ram[_dramAddr] : (byte)0;
            default:
                return base.ReadByte(port);
        }
    }

    /// <inheritdoc/>
    public override ushort ReadWord(ushort port) {
        RenderUpToNow();

        int highOffset = port - (_gusBase + 0x100);
        if (highOffset == 0x04) {
            return ReadFromRegister();
        }
        return base.ReadWord(port);
    }

    /// <inheritdoc/>
    public override void WriteByte(ushort port, byte value) {
        RenderUpToNow();

        int offset = port - _gusBase;
        switch (offset) {
            case 0x00: // mix control
                _mixControl = value;
                _shouldChangeIrqDma = true;
                return;
            case 0x08: // AdLib command mirror
                _adlibCommandReg = value;
                return;
            case 0x09: // timer control
                OnTimerControl(value);
                return;
            case 0x0B: // IRQ/DMA select
                OnIrqDmaSelect(value);
                return;
            default:
                break;
        }

        int highOffset = port - (_gusBase + 0x100);
        switch (highOffset) {
            case 0x02: // voice index
                _voiceIndex = (byte)(value & (MaxVoices - 1));
                _targetVoice = _voices[_voiceIndex];
                return;
            case 0x03: // GF1 register select
                _selectedReg = value;
                _registerData = 0;
                return;
            case 0x04: // data low byte – latch only, no register write yet
                _registerData = (ushort)((_registerData & 0xFF00) | value);
                return;
            case 0x05: // data high byte – latch then commit
                _registerData = (ushort)((_registerData & 0x00FF) | (value << 8));
                WriteToRegister();
                return;
            case 0x07: // DRAM byte
                if (_dramAddr < DramSizeBytes) {
                    _ram[_dramAddr] = value;
                }
                return;
            default:
                base.WriteByte(port, value);
                return;
        }
    }

    /// <inheritdoc/>
    public override void WriteWord(ushort port, ushort value) {
        RenderUpToNow();

        int highOffset = port - (_gusBase + 0x100);
        if (highOffset == 0x04) {
            _registerData = value;
            WriteToRegister();
            return;
        }
        base.WriteWord(port, value);
    }

    // Mixer callback

    private void MixerCallback(int framesRequested) {
        SoftwareMixer.PullFromQueueCallback<GravisUltraSound, AudioFrame>(framesRequested, this);
    }

    private void OnSchedulerTick() {
        _lastRenderedMs = _clock.ElapsedTimeMs;
        if (_channel.IsEnabled) {
            _frameCounter += _channel.FramesPerTick;
            int requestedFrames = (int)Math.Floor(_frameCounter);
            _frameCounter -= requestedFrames;
            if (requestedFrames > 0) {
                RenderFrames(requestedFrames);
            }
        }
        _scheduler.AddEvent(_tickEventHandler, 1, 0);
    }

    private void RenderUpToNow() {
        double now = _clock.ElapsedTimeMs;
        if (_channel.WakeUp()) {
            _lastRenderedMs = now;
            return;
        }

        int elapsedFrames = (int)Math.Floor((now - _lastRenderedMs) / _millisecondsPerFrame);
        if (elapsedFrames <= 0) {
            return;
        }

        RenderFrames(elapsedFrames);
        _lastRenderedMs += elapsedFrames * _millisecondsPerFrame;
    }

    private void RenderFrames(int framesRequested) {
        EnsureRenderBuf(framesRequested);
        Array.Clear(_renderBuf, 0, framesRequested);

        // Only render when the GF1 is running and the DAC is enabled
        if (IsRunning && IsDacEnabled) {
            for (int v = 0; v < _activeVoices; v++) {
                _voices[v].RenderFrames(_ram, _volScalars, _panScalars, _renderBuf, framesRequested);
            }
        }

        CheckVoiceIrqs();

        _outputQueue.NonblockingBulkEnqueue(_renderBuf.AsSpan(0, framesRequested), framesRequested);
    }

    // GF1 register reads

    private ushort ReadFromRegister() {
        // Global DSP registers
        switch (_selectedReg) {
            case 0x41: { // DMA control register – read clears TC IRQ
                    byte reg = _dmaControlReg;
                    if ((_irqStatus & IrqDmaFinished) != 0) {
                        reg |= DmaCtrlTcIrqPending;
                    }
                    _irqStatus &= unchecked((byte)~IrqDmaFinished);
                    RaiseInterruptRequest();
                    return (ushort)(reg << 8);
                }
            case 0x42: // DMA address register
                return _dmaAddr;
            case 0x45: // Timer control
                return (ushort)(_timerCtrl << 8);
            case 0x49: // DMA sampling control
                return (ushort)(_sampleCtrl << 8);
            case 0x4C: // Reset register
                return (ushort)(_resetReg << 8);
            case 0x8F: // Voice IRQ status
                return GetVoiceIrqStatus();
            default:
                break;
        }

        if (_targetVoice is null) {
            return _selectedReg is 0x80 or 0x8D ? (ushort)0x0300 : (ushort)0;
        }

        // Voice-specific registers
        GusVoice voice = _targetVoice;
        switch (_selectedReg) {
            case 0x80: // Voice wave control
                return (ushort)(voice.ReadWaveState() << 8);
            case 0x82: // Voice wave start MSW
                return (ushort)(voice.WaveStart >> 16);
            case 0x83: // Voice wave start LSW
                return (ushort)(voice.WaveStart & 0xFFFF);
            case 0x89: { // Voice volume position
                    int i = CeilSdivide(voice.VolPos, VolumeIncScalar);
                    i = Math.Max(0, Math.Min(i, VolLevels - 1));
                    return (ushort)(i << 4);
                }
            case 0x8A: // Voice wave current position MSW
                return (ushort)(voice.WavePos >> 16);
            case 0x8B: // Voice wave current position LSW
                return (ushort)(voice.WavePos & 0xFFFF);
            case 0x8D: // Voice volume control
                return (ushort)(voice.ReadVolState() << 8);
            default:
                return _registerData; // echo back last written value
        }
    }

    // GF1 register writes

    private void WriteToRegister() {
        // Global DSP registers
        switch (_selectedReg) {
            case 0x0E: { // Set number of active voices
                         // Jazz Jackrabbit reads back the register select from this write
                    _selectedReg = (byte)(_registerData >> 8);
                    byte num = (byte)(1 + ((_registerData >> 8) & 31));
                    SetActiveVoices(num);
                    return;
                }
            case 0x10: // Undocumented register (Fast Tracker 2)
                return;
            case 0x41: // DMA control register
                _dmaControlReg = (byte)(_registerData >> 8);
                _dmaSamples16Bit = (_dmaControlReg & DmaCtrlSamples16Bit) != 0;
                if ((_dmaControlReg & DmaCtrlEnabled) != 0) {
                    StartDmaTransfer();
                }
                return;
            case 0x42: // DMA DRAM address register
                _dmaAddr = _registerData;
                _dmaAddressNibble = 0;
                return;
            case 0x43: // DRAM address LSW (bits 0-15)
                _dramAddr = (_dramAddr & 0xF0000) | _registerData;
                return;
            case 0x44: // DRAM address MSW (bits 16-19 in upper nibble of high byte)
                _dramAddr = (_dramAddr & 0x0FFFF) | ((_registerData & 0x0F00) << 8);
                return;
            case 0x45: // Timer control register
                _timerCtrl = (byte)(_registerData >> 8);
                _timers[0].ShouldRaiseIrq = (_timerCtrl & IrqTimer1Bit) != 0;
                _timers[1].ShouldRaiseIrq = (_timerCtrl & IrqTimer2Bit) != 0;
                if (!_timers[0].ShouldRaiseIrq) {
                    _irqStatus &= unchecked((byte)~IrqTimer1Bit);
                }
                if (!_timers[1].ShouldRaiseIrq) {
                    _irqStatus &= unchecked((byte)~IrqTimer2Bit);
                }
                if (!_timers[0].ShouldRaiseIrq && !_timers[1].ShouldRaiseIrq) {
                    RaiseInterruptRequest();
                }
                return;
            case 0x46: // Timer 1 value
                _timers[0].Value = (byte)(_registerData >> 8);
                _timers[0].Delay = (0x100 - _timers[0].Value) * Timer1DefaultDelayMs;
                return;
            case 0x47: // Timer 2 value
                _timers[1].Value = (byte)(_registerData >> 8);
                _timers[1].Delay = (0x100 - _timers[1].Value) * Timer2DefaultDelayMs;
                return;
            case 0x49: // DMA sampling control register
                _sampleCtrl = (byte)(_registerData >> 8);
                if ((_sampleCtrl & 0x01) != 0) {
                    StartDmaTransfer();
                }
                return;
            case 0x4C: // Reset register
                _resetReg = (byte)(_registerData >> 8);
                if ((_resetReg & ResetRegIsRunning) == 0) {
                    DoReset();
                } else {
                    _channel.Enable(IsDacEnabled);
                }
                return;
            default:
                break;
        }

        if (_targetVoice is null) {
            return;
        }

        // Voice-specific registers
        GusVoice voice = _targetVoice;
        switch (_selectedReg) {
            case 0x00: // Voice wave control
                if (voice.UpdateWaveState((byte)(_registerData >> 8))) {
                    CheckVoiceIrqs();
                }
                break;
            case 0x01: // Voice wave rate
                voice.WriteWaveRate(_registerData);
                break;
            case 0x02: // Voice wave start MSW
                voice.WaveStart = UpdateWaveMsw(voice.WaveStart, _registerData);
                break;
            case 0x03: // Voice wave start LSW
                voice.WaveStart = UpdateWaveLsw(voice.WaveStart, _registerData);
                break;
            case 0x04: // Voice wave end MSW
                voice.WaveEnd = UpdateWaveMsw(voice.WaveEnd, _registerData);
                break;
            case 0x05: // Voice wave end LSW
                voice.WaveEnd = UpdateWaveLsw(voice.WaveEnd, _registerData);
                break;
            case 0x06: // Voice volume rate
                voice.WriteVolRate((byte)(_registerData >> 8));
                break;
            case 0x07: { // Voice volume start (EEEEMMMM format)
                    byte data = (byte)(_registerData >> 8);
                    voice.VolStart = (data << 4) * VolumeIncScalar;
                    break;
                }
            case 0x08: { // Voice volume end (EEEEMMMM format)
                    byte data = (byte)(_registerData >> 8);
                    voice.VolEnd = (data << 4) * VolumeIncScalar;
                    break;
                }
            case 0x09: // Voice volume current position
                voice.VolPos = (_registerData >> 4) * VolumeIncScalar;
                break;
            case 0x0A: // Voice wave current position MSW
                voice.WavePos = UpdateWaveMsw(voice.WavePos, _registerData);
                break;
            case 0x0B: // Voice wave current position LSW
                voice.WavePos = UpdateWaveLsw(voice.WavePos, _registerData);
                break;
            case 0x0C: // Voice pan pot
                voice.WritePanPot((byte)(_registerData >> 8));
                break;
            case 0x0D: // Voice volume control
                if (voice.UpdateVolState((byte)(_registerData >> 8))) {
                    CheckVoiceIrqs();
                }
                break;
        }
    }

    // Wave address helpers (match dosbox UpdateWaveMsw / UpdateWaveLsw)

    private static int UpdateWaveMsw(int addr, ushort regData) {
        // Keep bits 0-15 of addr; replace bits 16-28 with the 13-bit regData value.
        int lower = addr & 0x0000FFFF;
        int upper = (regData & 0x1FFF) << 16;
        return lower | upper;
    }

    private static int UpdateWaveLsw(int addr, ushort regData) {
        // Keep bits 16-31 of addr; replace bits 0-15 with regData.
        int upper = addr & unchecked((int)0xFFFF0000);
        return upper | regData;
    }

    // Timer handling

    private void OnTimerControl(byte value) {
        if ((value & 0x80) != 0) {
            // Reset timer expired flags
            _timers[0].HasExpired = false;
            _timers[1].HasExpired = false;
            return;
        }

        _timers[0].IsMasked = (value & 0x40) != 0;
        _timers[1].IsMasked = (value & 0x20) != 0;

        for (int t = 0; t < _timers.Length; t++) {
            GusTimer timer = _timers[t];
            bool start = (value & (1 << t)) != 0;
            if (start && !timer.IsCountingDown) {
                timer.IsCountingDown = true;
                timer.HasExpired = false;
                _scheduler.AddEvent(_timerEventHandlers[t], timer.Delay, 0);
            } else if (!start) {
                timer.IsCountingDown = false;
            }
        }
    }

    private void OnTimerExpired(int timerIndex) {
        GusTimer timer = _timers[timerIndex];
        if (!timer.IsCountingDown) { return; }
        if (!timer.IsMasked) {
            timer.HasExpired = true;
        }
        if (timer.ShouldRaiseIrq) {
            _irqStatus |= timerIndex == 0 ? IrqTimer1Bit : IrqTimer2Bit;
            RaiseInterruptRequest();
        }
        if (timer.IsCountingDown) {
            _scheduler.AddEvent(_timerEventHandlers[timerIndex], timer.Delay, 0);
        }
    }

    private byte GetTimerStatus() {
        byte status = 0;
        if (_timers[0].HasExpired) { status |= 0x40; }
        if (_timers[1].HasExpired) { status |= 0x20; }
        if ((status & 0x60) != 0) { status |= 0x80; } // combined expired bit
        if ((_irqStatus & IrqTimer1Bit) != 0) { status |= 0x04; }
        if ((_irqStatus & IrqTimer2Bit) != 0) { status |= 0x02; }
        return status;
    }

    // IRQ/DMA port 0x20B selection

    private void OnIrqDmaSelect(byte value) {
        if (!_shouldChangeIrqDma) {
            return;
        }
        _shouldChangeIrqDma = false;

        byte ch1Selector = (byte)(value & 0x07);
        byte ch2Selector = (byte)((value >> 3) & 0x07);
        bool combineChannels = (value & 0x40) != 0;

        if ((_mixControl & MixCtrlIrqCtrlSelected) != 0) {
            if (ch1Selector < IrqAddresses.Length && IrqAddresses[ch1Selector] != 0) {
                ChangeIrq(IrqAddresses[ch1Selector]);
            }
            if (combineChannels && ch2Selector == 0) {
                _irq2 = _irq;
            } else if (ch2Selector < IrqAddresses.Length && IrqAddresses[ch2Selector] != 0) {
                _irq2 = ToInternalIrq(IrqAddresses[ch2Selector]);
            }
        } else {
            if (ch1Selector < DmaAddresses.Length && DmaAddresses[ch1Selector] != 0) {
                byte newDma = DmaAddresses[ch1Selector];
                UpdatePlaybackDmaAddress(newDma);
            }
            if (combineChannels && ch2Selector == 0) {
                UpdateRecordingDmaAddress(_dma);
            } else if (ch2Selector < DmaAddresses.Length && DmaAddresses[ch2Selector] != 0) {
                UpdateRecordingDmaAddress(DmaAddresses[ch2Selector]);
            }
        }
    }

    // IRQ handling

    private byte GetIrqStatus() {
        byte result = _irqStatus;
        _irqStatus = 0;
        _irqPreviouslyInterrupted = false;
        _dualPic.DeactivateIrq(_irq);
        return result;
    }

    private ushort GetVoiceIrqStatus() {
        // Returns the voice index with pending IRQ and clears its bits.
        // Bit 5 is always set; bit 6 clear = vol IRQ, bit 7 clear = wave IRQ.
        byte reg = (byte)(_voiceIrq.Status | 0x20);
        uint mask = 1u << _voiceIrq.Status;

        if ((_voiceIrq.VolState & mask) == 0) {
            reg |= 0x40; // no vol IRQ for this voice
        }
        if ((_voiceIrq.WaveState & mask) == 0) {
            reg |= 0x80; // no wave IRQ for this voice
        }

        _voiceIrq.VolState &= ~mask;
        _voiceIrq.WaveState &= ~mask;
        CheckVoiceIrqs();
        return (ushort)(reg << 8);
    }

    private void CheckVoiceIrqs() {
        // Clear voice IRQ bits in irq_status then re-evaluate
        _irqStatus &= 0x9F;
        uint totalMask = (_voiceIrq.VolState | _voiceIrq.WaveState) & _activeVoiceMask;

        if (totalMask == 0) {
            RaiseInterruptRequest();
            return;
        }

        if (_voiceIrq.VolState != 0) {
            _irqStatus |= IrqVolStateBit;
        }
        if (_voiceIrq.WaveState != 0) {
            _irqStatus |= IrqWaveStateBit;
        }

        RaiseInterruptRequest();

        // Advance status to the next voice with a pending IRQ
        while ((totalMask & (1u << _voiceIrq.Status)) == 0) {
            _voiceIrq.Status++;
            if (_voiceIrq.Status >= _activeVoices) {
                _voiceIrq.Status = 0;
            }
        }
    }

    // Reset

    private void DoReset() {
        _channel.Enable(false);

        foreach (Spice86.Core.Emulator.VM.DeviceScheduler.EventHandler timerEventHandler in _timerEventHandlers) {
            _scheduler.RemoveEvents(timerEventHandler);
        }
        _scheduler.RemoveEvents(_dmaEventHandler);

        _irqStatus = 0;
        _irqPreviouslyInterrupted = false;
        _adlibCommandReg = AdlibCmdDefault;
        _dmaControlReg = 0;
        _dmaSamples16Bit = false;
        _sampleCtrl = 0;
        _timerCtrl = 0;
        _timers[0] = new GusTimer(Timer1DefaultDelayMs);
        _timers[1] = new GusTimer(Timer2DefaultDelayMs);

        for (int v = 0; v < MaxVoices; v++) {
            _voices[v].ResetCtrls();
        }

        _voiceIrq.VolState = 0;
        _voiceIrq.WaveState = 0;
        _voiceIrq.Status = 0;
        _targetVoice = null;
        _voiceIndex = 0;

        UpdateDmaAddr(0);
        _dramAddr = 0;
        _registerData = 0;
        _selectedReg = 0;
        _shouldChangeIrqDma = false;
        _mixControl = MixControlDefault;
        _activeVoices = MinVoices;
        _activeVoiceMask = 0xFFFFFFFFu >> (MaxVoices - MinVoices);
        _lastRenderedMs = _clock.ElapsedTimeMs;
    }

    // DMA handling

    private void StartDmaTransfer() {
        _scheduler.RemoveEvents(_dmaEventHandler);
        _scheduler.AddEvent(_dmaEventHandler, DmaTransferDelayMs, 0);
    }

    private void UpdatePlaybackDmaAddress(byte newDma) {
        if (newDma == _dma) {
            return;
        }

        _scheduler.RemoveEvents(_dmaEventHandler);
        DmaChannel? oldChannel = _dmaBus.GetChannel(_dma);
        oldChannel?.Reset();
        _dma = newDma;
        DmaChannel? newChannel = _dmaBus.GetChannel(newDma);
        newChannel?.ReserveFor("GravisUltraSound", OnDmaChannelEvicted);
        newChannel?.RegisterCallback(OnDmaEvent);
    }

    private void UpdateRecordingDmaAddress(byte newDma) {
        _dma2 = newDma;
    }

    private void OnDmaEvent(DmaChannel channel, DmaChannel.DmaEvent evt) {
        if (evt == DmaChannel.DmaEvent.IsUnmasked) {
            StartDmaTransfer();
        }
    }

    private void OnDmaChannelEvicted() {
        // DMA was forcibly released by another device.
    }

    private void ProcessDmaTransfer() {
        DmaChannel? channel = _dmaBus.GetChannel(_dma);
        if (channel is not null && PerformDmaTransfer(channel)) {
            _scheduler.AddEvent(_dmaEventHandler, DmaTransferDelayMs, 0);
        }
    }

    private bool PerformDmaTransfer(DmaChannel channel) {
        if (channel.IsMasked || (_dmaControlReg & DmaCtrlEnabled) == 0) {
            return false;
        }

        Span<byte> chunk = stackalloc byte[DmaTransferSizeBytes];
        int chunkWords = Math.Min(DmaTransferSizeBytes >> channel.ShiftCount, channel.CurrentCount + 1);

        uint dmaOffset = GetDmaOffset();
        bool invert = (_dmaControlReg & DmaCtrlInvertHighBit) != 0;
        bool samples16 = _dmaSamples16Bit;
        bool gusToHost = (_dmaControlReg & DmaCtrlGusToHost) != 0;

        if (gusToHost) {
            int bytesToTransfer = chunkWords << channel.ShiftCount;
            for (int i = 0; i < bytesToTransfer; i++) {
                chunk[i] = _ram[(int)((dmaOffset + (uint)i) & 0xFFFFF)];
            }
            int wordsWritten = channel.Write(chunkWords, chunk.Slice(0, bytesToTransfer));
            dmaOffset = (dmaOffset + (uint)(wordsWritten << channel.ShiftCount)) & 0xFFFFF;
        } else {
            int wordsRead = channel.Read(chunkWords, chunk);
            int bytesRead = wordsRead << channel.ShiftCount;

            for (int i = 0; i < bytesRead; i++) {
                int dest = (int)((dmaOffset + (uint)i) & 0xFFFFF);
                byte sample = chunk[i];
                if (invert && (!samples16 || ((dmaOffset + (uint)i) & 1) != 0)) {
                    sample ^= 0x80;
                }
                _ram[dest] = sample;
            }

            dmaOffset = (dmaOffset + (uint)bytesRead) & 0xFFFFF;
        }

        UpdateDmaAddr(dmaOffset);

        // The DMA terminal-count IRQ only fires when the channel actually reached TC.
        if (channel.HasReachedTerminalCount && (_dmaControlReg & DmaCtrlWantsIrqOnTc) != 0) {
            _irqStatus |= IrqDmaFinished;
            RaiseInterruptRequest();
        }

        return !channel.HasReachedTerminalCount;
    }

    private uint GetDmaOffset() {
        uint adjusted;
        if (IsDmaXfer16Bit()) {
            uint upper = (uint)_dmaAddr & 0xC000u;
            uint lower = (uint)_dmaAddr & 0x1FFFu;
            adjusted = upper | (lower << 1);
        } else {
            adjusted = _dmaAddr;
        }
        return (adjusted << 4) + _dmaAddressNibble;
    }

    private void UpdateDmaAddr(uint offset) {
        uint adjusted;
        if (IsDmaXfer16Bit()) {
            uint upper = offset & 0xC0000u;
            uint lower = offset & 0x3FFFEu;
            adjusted = upper | (lower >> 1);
        } else {
            adjusted = offset & 0xFFFF0u;
        }
        _dmaAddr = (ushort)(adjusted >> 4);
        _dmaAddressNibble = (byte)(adjusted & 0x0F);
    }

    private bool IsDmaXfer16Bit() {
        return (_dmaControlReg & DmaCtrlChannel16Bit) != 0 && _dma >= 4;
    }

    // Voice and sample-rate management

    private void SetActiveVoices(byte count) {
        int clamped = Math.Max(MinVoices, Math.Min(MaxVoices, (int)count));
        if (clamped != _activeVoices) {
            _activeVoices = clamped;
            _activeVoiceMask = 0xFFFFFFFFu >> (MaxVoices - _activeVoices);
            _channel.SampleRate = GetSampleRate();
            _millisecondsPerFrame = 1000.0 / GetSampleRate();
        }
    }

    private int GetSampleRate() {
        return (int)(1000000.0 / (1.619695497 * _activeVoices));
    }

    private static byte ToInternalIrq(byte irq) => irq == 2 ? (byte)9 : irq;

    private static byte ToExternalIrq(byte irq) => irq == 9 ? (byte)2 : irq;

    // Lookup-table construction

    private void BuildVolScalars() {
        // Build the table from the end downward, dividing by (1 + DeltaDb) each step.
        // This produces constant-dB spacing, matching the GUS hardware.
        double scalar = 1.0;
        double divisor = 1.0 + DeltaDb;
        for (int i = VolLevels - 1; i >= 1; i--) {
            _volScalars[i] = (float)scalar;
            scalar /= divisor;
        }
        _volScalars[0] = 0.0f;
    }

    private void BuildPanScalars() {
        // Constant-power panning: positions 0-15 map from full-left to full-right,
        // with position 7 exactly at centre. The asymmetric normalization (÷7 vs ÷8)
        // keeps position 7 at exactly π/4.
        for (int p = 0; p < PanPositions; p++) {
            double norm = (p - 7.0) / (p < 7 ? 7.0 : 8.0);
            double angle = (norm + 1.0) * Math.PI / 4.0;
            _panScalars[p] = new AudioFrame(
                (float)Math.Cos(angle),
                (float)Math.Sin(angle));
        }
    }

    // Port registration

    private void InitPortHandlers(IOPortDispatcher dispatcher, int portBase) {
        // Low group (around gusBase)
        dispatcher.AddIOPortHandler((ushort)(0x200 + portBase), this);  // mix control
        dispatcher.AddIOPortHandler((ushort)(0x206 + portBase), this);  // IRQ status
        dispatcher.AddIOPortHandler((ushort)(0x208 + portBase), this);  // timer status / AdLib mirror
        dispatcher.AddIOPortHandler((ushort)(0x209 + portBase), this);  // timer control
        dispatcher.AddIOPortHandler((ushort)(0x20A + portBase), this);  // adlib command mirror
        dispatcher.AddIOPortHandler((ushort)(0x20B + portBase), this);  // IRQ/DMA select

        // High group (gusBase + 0x100)
        dispatcher.AddIOPortHandler((ushort)(0x302 + portBase), this);  // voice index
        dispatcher.AddIOPortHandler((ushort)(0x303 + portBase), this);  // GF1 reg select
        dispatcher.AddIOPortHandler((ushort)(0x304 + portBase), this);  // data word
        dispatcher.AddIOPortHandler((ushort)(0x305 + portBase), this);  // data high byte
        dispatcher.AddIOPortHandler((ushort)(0x307 + portBase), this);  // DRAM byte
    }

    // Helpers

    private void EnsureRenderBuf(int size) {
        if (_renderBuf.Length < size) {
            _renderBuf = new AudioFrame[size];
        }
    }

    private static int CeilSdivide(int a, int b) {
        if (b == 0) { return 0; }
        if (a >= 0) { return (a + b - 1) / b; }
        return a / b;
    }
}
