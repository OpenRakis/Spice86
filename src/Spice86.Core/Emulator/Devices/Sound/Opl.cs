namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Libs.Sound.Common;
using Spice86.Libs.Sound.Devices.AdlibGold;
using Spice86.Libs.Sound.Devices.NukedOpl3;
using Spice86.Shared.Interfaces;

using System.Threading;

/// <summary>
///     Virtual device which emulates OPL FM sound.
/// </summary>
public class Opl : DefaultIOPortHandler, IDisposable {
    private readonly AdLibGoldDevice? _adLibGold;
    private readonly AdLibGoldIo? _adLibGoldIo;
    private readonly Opl3Chip _chip = new();
    private readonly Lock _chipLock = new();
    private readonly EmulationLoopScheduler _scheduler;
    private readonly IEmulatedClock _clock;
    private readonly DualPic _dualPic;
    private readonly OplIo _oplIo;
    private readonly byte _oplIrqLine;
    private readonly bool _useAdLibGold;
    
    // FIFO queue for cycle-accurate OPL frame generation
    private readonly Queue<AudioFrame> _fifo = new();
    
    // Time tracking for cycle-accurate rendering
    private double _lastRenderedMs;
    private readonly double _msPerFrame;

    /// <summary>
    ///     The mixer channel used for the OPL synth.
    /// </summary>
    private readonly MixerChannel _mixerChannel;

    private readonly bool _useOplIrq;
    private bool _disposed;

    /// <summary>
    ///     Initializes a new instance of the OPL synth chip.
    /// </summary>
    /// <param name="mixer">The global software mixer used to create the opl channel and request frames.</param>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="ioPortDispatcher">
    ///     The class that is responsible for dispatching ports reads and writes to classes that
    ///     respond to them.
    /// </param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O port wasn't handled.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="scheduler">The event scheduler.</param>
    /// <param name="clock">The emulated clock.</param>
    /// <param name="dualPic">The shared dual PIC scheduler.</param>
    /// <param name="useAdlibGold">True to enable AdLib Gold filtering and surround processing.</param>
    /// <param name="enableOplIrq">True to forward OPL IRQs to the PIC.</param>
    /// <param name="oplIrqLine">IRQ line used when OPL IRQs are enabled.</param>
    public Opl(Mixer mixer, State state,
        IOPortDispatcher ioPortDispatcher, bool failOnUnhandledPort,
        ILoggerService loggerService, EmulationLoopScheduler scheduler, IEmulatedClock clock, DualPic dualPic,
        bool useAdlibGold = false, bool enableOplIrq = false, byte oplIrqLine = 5)
        : base(state, failOnUnhandledPort, loggerService) {
        mixer.LockMixerThread();

        HashSet<ChannelFeature> features = new HashSet<ChannelFeature> {
            ChannelFeature.Sleep,
            ChannelFeature.FadeOut,
            ChannelFeature.NoiseGate,
            ChannelFeature.ReverbSend,
            ChannelFeature.ChorusSend,
            ChannelFeature.Synthesizer,
            ChannelFeature.Stereo
        };
        _mixerChannel = mixer.AddChannel(AudioCallback, 49716, nameof(Opl), features);

        _mixerChannel.SetResampleMethod(ResampleMethod.Resample);

        _scheduler = scheduler;
        _clock = clock;
        _dualPic = dualPic;
        _useAdLibGold = useAdlibGold;
        _useOplIrq = enableOplIrq;
        _oplIrqLine = oplIrqLine;

        _oplIo = new OplIo(_chip, () => _clock.ElapsedTimeMs) {
            OnIrqChanged = OnOplIrqChanged
        };

        int sampleRate = _mixerChannel.GetSampleRate();
        if (_useAdLibGold) {
            _adLibGold = new AdLibGoldDevice(sampleRate, loggerService);
            _adLibGoldIo = _adLibGold.CreateIoAttachedTo(_oplIo);
        }

        _loggerService.Debug(
            "Initializing OPL FM synth. AdLib Gold enabled: {AdLibGoldEnabled}, OPL IRQ enabled: {OplIrqEnabled}, Sample rate: {SampleRate}",
            _useAdLibGold, _useOplIrq, sampleRate);

        _oplIo.Reset((uint)sampleRate);

        const float OplVolumeGain = 1.5f;
        _mixerChannel.Set0dbScalar(OplVolumeGain);

        const float thresholdDb = -65.0f + 3.52f;
        const float attackTimeMs = 1.0f;
        const float releaseTimeMs = 100.0f;
        _mixerChannel.ConfigureNoiseGate(thresholdDb, attackTimeMs, releaseTimeMs);
        
        _mixerChannel.EnableNoiseGate(true);

        const double MillisInSecond = 1000.0;
        _msPerFrame = MillisInSecond / 49716;
        _lastRenderedMs = _clock.ElapsedTimeMs;

        InitializeToneGenerators();

        InitPortHandlers(ioPortDispatcher);

        mixer.UnlockMixerThread();
    }

    /// <summary>
    ///     Exposes the opl mixer channel for other components (e.g., SoundBlaster hardware mixer).
    /// </summary>
    public MixerChannel MixerChannel => _mixerChannel;

    /// <inheritdoc />
    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Gets a value indicating whether AdLib Gold processing is enabled.
    /// </summary>
    public bool IsAdlibGoldEnabled => _adLibGoldIo is not null;

    /// <summary>
    ///     Initializes default envelopes and rates for the opl operators.
    /// </summary>
    private void InitializeToneGenerators() {
        int[] fourOp = [0, 1, 2, 6, 7, 8, 12, 13, 14];
        foreach (int index in fourOp) {
            Opl3Operator slot = _chip.Slots[index];
            slot.EnvelopeGeneratorOutput = 511;
            slot.EnvelopeGeneratorLevel = 571;
            slot.EnvelopeGeneratorState = (byte)EnvelopeGeneratorStage.Sustain;
            slot.RegFrequencyMultiplier = 1;
            slot.RegKeyScaleLevel = 1;
            slot.RegTotalLevel = 15;
            slot.RegAttackRate = 15;
            slot.RegDecayRate = 1;
            slot.RegSustainLevel = 5;
            slot.RegReleaseRate = 3;
        }

        int[] twoOp = [3, 4, 5, 9, 10, 11, 15, 16, 17];
        foreach (int index in twoOp) {
            Opl3Operator slot = _chip.Slots[index];
            slot.EnvelopeGeneratorOutput = 511;
            slot.EnvelopeGeneratorLevel = 511;
            slot.EnvelopeGeneratorState = (byte)EnvelopeGeneratorStage.Sustain;
            slot.RegKeyScaleRate = 1;
            slot.RegFrequencyMultiplier = 1;
            slot.RegAttackRate = 15;
            slot.RegDecayRate = 2;
            slot.RegSustainLevel = 7;
            slot.RegReleaseRate = 4;
        }
    }

    /// <summary>
    ///     Registers this device for the opl and AdLib Gold I/O port ranges.
    /// </summary>
    /// <param name="ioPortDispatcher">Dispatcher used to route port accesses.</param>
    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(IOplPort.PrimaryAddressPortNumber, this);
        ioPortDispatcher.AddIOPortHandler(IOplPort.PrimaryDataPortNumber, this);
        ioPortDispatcher.AddIOPortHandler(IOplPort.SecondaryAddressPortNumber, this);
        ioPortDispatcher.AddIOPortHandler(IOplPort.SecondaryDataPortNumber, this);
        
        
        if (_adLibGoldIo is not null) {
            ioPortDispatcher.AddIOPortHandler(IOplPort.AdLibGoldAddressPortNumber, this);
            ioPortDispatcher.AddIOPortHandler(IOplPort.AdLibGoldDataPortNumber, this);
        }
    }

    /// <summary>
    ///     Releases resources held by the OPL FM device.
    /// </summary>
    /// <param name="disposing">True when called from <see cref="Dispose()" />; false during finalization.</param>
    private void Dispose(bool disposing) {
        if (_disposed) {
            return;
        }

        if (disposing) {
            if (_useOplIrq) {
                _dualPic.DeactivateIrq(_oplIrqLine);
            }

            _adLibGold?.Dispose();
        }

        _disposed = true;
    }

    /// <inheritdoc />
    /// <summary>
    ///     Reads from opl or AdLib Gold I/O ports.
    /// </summary>
    public override byte ReadByte(ushort port) {
        lock (_chipLock) {
            _oplIo.AdvanceTimersOnly(_clock.ElapsedTimeMs);

            ushort targetPort = port;
            return targetPort switch {
                IOplPort.PrimaryAddressPortNumber => _oplIo.ReadPort(targetPort),
                IOplPort.PrimaryDataPortNumber => _oplIo.ReadPort(targetPort),
                IOplPort.SecondaryAddressPortNumber => _oplIo.ReadPort(targetPort),
                IOplPort.SecondaryDataPortNumber => _oplIo.ReadPort(targetPort),
                IOplPort.AdLibGoldAddressPortNumber when _adLibGoldIo is not null => _oplIo.ReadPort(targetPort),
                IOplPort.AdLibGoldDataPortNumber when _adLibGoldIo is not null => _oplIo.ReadPort(targetPort),
                _ => 0xFF
            };
        }
    }

    /// <inheritdoc />
    /// <summary>
    ///     Writes to opl or AdLib Gold I/O ports.
    /// </summary>
    public override void WriteByte(ushort port, byte value) {
        RenderUpToNow();
        
        _mixerChannel.WakeUp();

        lock (_chipLock) {
            _oplIo.AdvanceTimersOnly(_clock.ElapsedTimeMs);

            switch (port) {
                case IOplPort.PrimaryAddressPortNumber:
                case IOplPort.SecondaryAddressPortNumber:
                case IOplPort.PrimaryDataPortNumber:
                case IOplPort.SecondaryDataPortNumber:
                    _oplIo.WritePort(port, value);
                    break;
                case IOplPort.AdLibGoldAddressPortNumber:
                case IOplPort.AdLibGoldDataPortNumber:
                    if (_adLibGoldIo is not null) {
                        _oplIo.WritePort(port, value);
                    }
                    break;
                default:
                    LogUnhandledPortWrite(port, value);
                    return;
            }
        }
    }

    /// <inheritdoc />
    public override void WriteWord(ushort port, ushort value) {
        if (port is not (IOplPort.PrimaryAddressPortNumber or IOplPort.SecondaryAddressPortNumber)) {
            return;
        }

        WriteByte(port, (byte)value);
        WriteByte((ushort)(port + 1), (byte)(value >> 8));
    }

    /// <summary>
    ///     opl mixer handler - called by the mixer thread to generate frames.
    /// </summary>
    public void AudioCallback(int framesRequested) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("opl: AudioCallback framesRequested={Frames}, FIFO size={FifoSize}", 
                framesRequested, _fifo.Count);
        }
        
        lock (_chipLock) {
            int framesRemaining = framesRequested;
            Span<float> frameData = stackalloc float[2];
            
            while (framesRemaining > 0 && _fifo.Count > 0) {
                AudioFrame frame = _fifo.Dequeue();
                frameData[0] = frame.Left;
                frameData[1] = frame.Right;
                _mixerChannel.AddSamples_sfloat(1, frameData);
                framesRemaining--;
            }
            
            // If we run out of FIFO frames, we simply stop adding samples.
            // This constitutes an underrun, which the mixer will handle (e.g. by outputting silence).
            // We do NOT generate "future" frames here because the emulation hasn't reached that point yet.
            
            _lastRenderedMs = _clock.ElapsedTimeMs;
        }

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("opl: AudioCallback completed, FIFO size now={FifoSize}", _fifo.Count);
        }
    }
    
    /// <summary>
    ///     Renders cycle-accurate OPL frames up to the current emulated time.
    ///     Called on every port write to maintain synchronization between CPU and audio.
    /// </summary>
    private void RenderUpToNow() {
        double now = _clock.ElapsedTimeMs;
        
        if (_mixerChannel.WakeUp()) {
            _lastRenderedMs = now;
            return;
        }
        
        while (_lastRenderedMs < now) {
            _lastRenderedMs += _msPerFrame;
            AudioFrame frame = RenderSingleFrame();
            _fifo.Enqueue(frame);
        }
    }
    
    /// <summary>
    ///     Renders a single OPL audio frame.
    /// </summary>
    private AudioFrame RenderSingleFrame() {
        Span<short> buf = stackalloc short[2];
        
        _chip.GenerateStream(buf);
        
        AudioFrame frame;
        
        if (_adLibGold is not null) {
            Span<float> floatBuf = stackalloc float[2];
            _adLibGold.Process(buf, 1, floatBuf);
            frame = new AudioFrame { Left = floatBuf[0], Right = floatBuf[1] };
        } else {
            frame = new AudioFrame { Left = (float)buf[0], Right = (float)buf[1] };
        }
        
        return frame;
    }
    
    /// <summary>
    ///     Handles changes in the OPL IRQ line state.
    ///     OPL timers can trigger IRQs when enabled (not default behavior).
    /// </summary>
    /// <param name="asserted">True when the OPL IRQ is asserted.</param>
    private void OnOplIrqChanged(bool asserted) {
        if (!_useOplIrq) {
            return;
        }

        if (asserted) {
            _dualPic.ActivateIrq(_oplIrqLine);
        } else {
            _dualPic.DeactivateIrq(_oplIrqLine);
        }
    }
}
