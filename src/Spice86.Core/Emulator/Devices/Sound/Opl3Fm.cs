namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Libs.Sound.Common;
using Spice86.Libs.Sound.Devices.AdlibGold;
using Spice86.Libs.Sound.Devices.NukedOpl3;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Threading;

/// <summary>
///     Virtual device which emulates OPL3 FM sound.
///     Mirrors DOSBox Staging Opl class from src/hardware/audio/opl.cpp:84-163 and opl.h:84-163
/// </summary>
public class Opl3Fm : DefaultIOPortHandler, IDisposable {
    private const int MaxSamplesPerGenerationBatch = 512;
    private readonly AdLibGoldDevice? _adLibGold;
    private readonly AdLibGoldIo? _adLibGoldIo;
    private readonly Opl3Chip _chip = new();
    private readonly Lock _chipLock = new();
    private readonly EmulationLoopScheduler _scheduler;
    private readonly IEmulatedClock _clock;
    private readonly DualPic _dualPic;
    private readonly Opl3Io _oplIo;
    private readonly byte _oplIrqLine;
    private readonly EventHandler _oplTimerHandler;
    private readonly float[] _playBuffer = new float[2048];
    private readonly bool _useAdLibGold;
    
    // FIFO queue for cycle-accurate OPL frame generation
    // Mirrors DOSBox Staging opl.h:103
    private readonly Queue<AudioFrame> _fifo = new();
    
    // Time tracking for cycle-accurate rendering
    // Mirrors DOSBox Staging opl.h:122-123
    private double _lastRenderedMs;
    private double _msPerFrame;

    /// <summary>
    ///     The mixer channel used for the OPL3 FM synth.
    /// </summary>
    private readonly MixerChannel _mixerChannel;

    private readonly short[] _tmpInterleaved = new short[2048];
    private readonly bool _useOplIrq;
    private bool _disposed;
    private bool _oplTimerScheduled;

    /// <summary>
    ///     Initializes a new instance of the OPL3 FM synth chip.
    ///     Mirrors DOSBox Staging Opl::Opl() constructor from opl.cpp:812-942
    /// </summary>
    /// <param name="mixer">The global software mixer used to create the OPL3 channel and request frames.</param>
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
    public Opl3Fm(Mixer mixer, State state,
        IOPortDispatcher ioPortDispatcher, bool failOnUnhandledPort,
        ILoggerService loggerService, EmulationLoopScheduler scheduler, IEmulatedClock clock, DualPic dualPic,
        bool useAdlibGold = false, bool enableOplIrq = false, byte oplIrqLine = 5)
        : base(state, failOnUnhandledPort, loggerService) {
        // Lock mixer thread during construction to prevent concurrent modifications
        // Mirrors DOSBox Staging opl.cpp:816 (MIXER_LockMixerThread)
        mixer.LockMixerThread();

        // Create and register the OPL3 mixer channel
        // Mirrors DOSBox Staging opl.cpp:825-846 (channel_features and MIXER_AddChannel)
        // Features: Sleep (CPU efficiency), FadeOut (smooth stop), NoiseGate (residual noise removal),
        //           ReverbSend (reverb effect), ChorusSend (chorus effect), Synthesizer (FM synth), Stereo (OPL3)
        HashSet<ChannelFeature> features = new HashSet<ChannelFeature> {
            ChannelFeature.Sleep,
            ChannelFeature.FadeOut,
            ChannelFeature.NoiseGate,
            ChannelFeature.ReverbSend,
            ChannelFeature.ChorusSend,
            ChannelFeature.Synthesizer,
            ChannelFeature.Stereo  // OPL3 is stereo (dual_opl in DOSBox)
        };
        _mixerChannel = mixer.AddChannel(framesRequested => AudioCallback(framesRequested), 49716, "OPL3FM", features);

        // Set resample method to always use Speex resampling (no upsampling)
        // Mirrors DOSBox Staging opl.cpp:848
        _mixerChannel.SetResampleMethod(ResampleMethod.Resample);

        _scheduler = scheduler;
        _clock = clock;
        _dualPic = dualPic;
        _useAdLibGold = useAdlibGold;
        _useOplIrq = enableOplIrq;
        _oplIrqLine = oplIrqLine;

        _oplTimerHandler = ServiceOplTimers;

        _oplIo = new Opl3Io(_chip, () => _clock.ElapsedTimeMs) {
            OnIrqChanged = OnOplIrqChanged
        };

        int sampleRate = _mixerChannel.GetSampleRate();
        // Mirrors DOSBox Staging opl.cpp:289-290 (AdlibGold initialization for Opl3Gold mode)
        if (_useAdLibGold) {
            _adLibGold = new AdLibGoldDevice(sampleRate, loggerService);
            _adLibGoldIo = _adLibGold.CreateIoAttachedTo(_oplIo);
        }

        _loggerService.Debug(
            "Initializing OPL3 FM synth. AdLib Gold enabled: {AdLibGoldEnabled}, OPL IRQ enabled: {OplIrqEnabled}, Sample rate: {SampleRate}",
            _useAdLibGold, _useOplIrq, sampleRate);

        // Mirrors DOSBox Staging opl.cpp:267 (OPL3_Reset call in Init())
        _oplIo.Reset((uint)sampleRate);

        // Set OPL volume gain to 1.5x
        // Mirrors DOSBox Staging opl.cpp:850-863
        // This effectively adds a 1.5x gain factor to OPL output.
        // Used to be 2.0, which was measured to be too high. Exact value depends on card/clone.
        // CRITICAL: Don't touch this value as many people fine-tune their mixer volumes per game.
        const float OplVolumeGain = 1.5f;
        _mixerChannel.Set0dbScalar(OplVolumeGain);

        // Configure noise gate to remove OPL chip residual noise
        // Mirrors DOSBox Staging opl.cpp:865-899
        // Gets rid of residual noise in [-8, 0] range on OPL2 and [-18, 0] range on OPL3
        // This is accurate hardware behavior but annoying - OPL chips use bitwise inversion
        // for negative sine, causing small oscillations even when envelope generator is muted.
        // Threshold is fine-tuned to remove noise while leaving low level signals intact.
        // gain_to_decibel(1.5f) = 20 * log10(1.5) ≈ 3.52dB
        const float thresholdDb = -65.0f + 3.52f; // -65.0f + gain_to_decibel(OplVolumeGain)
        const float attackTimeMs = 1.0f;
        const float releaseTimeMs = 100.0f;
        _mixerChannel.ConfigureNoiseGate(thresholdDb, attackTimeMs, releaseTimeMs);
        
        // Enable noise gate by default for OPL (mirrors DOSBox denoiser setting)
        // In DOSBox this is controlled by mixer's "denoiser" setting, we enable it by default
        _mixerChannel.EnableNoiseGate(true);

        // Initialize cycle-accurate timing for FIFO rendering
        // Mirrors DOSBox Staging opl.cpp:272
        const double MillisInSecond = 1000.0;
        _msPerFrame = MillisInSecond / 49716; // OplSampleRateHz
        _lastRenderedMs = _clock.ElapsedTimeMs;

        // DON'T enable the channel here - it starts disabled and wakes up on first port write
        // Mirrors DOSBox Staging opl.cpp:843-846 where MIXER_AddChannel doesn't call Enable(true)
        // The channel will be enabled by WakeUp() call in WriteByte() when OPL ports are accessed
        // This follows the WakeUp pattern from opl.cpp:423 (RenderUpToNow calls channel->WakeUp())

        // Mirrors DOSBox Staging opl.cpp:269 (initialize_opl_tone_generators call in Init())
        InitializeToneGenerators();

        InitPortHandlers(ioPortDispatcher);

        // Unlock mixer thread after construction completes
        // Mirrors DOSBox Staging opl.cpp:941 (MIXER_UnlockMixerThread)
        mixer.UnlockMixerThread();
    }

    /// <summary>
    ///     Exposes the OPL3 mixer channel for other components (e.g., SoundBlaster hardware mixer).
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
    ///     Initializes default envelopes and rates for the OPL3 operators.
    ///     Mirrors DOSBox Staging initialize_opl_tone_generators() from opl.cpp:64-97
    /// </summary>
    private void InitializeToneGenerators() {
        // First 9 operators used for 4-op FM synthesis
        // Mirrors DOSBox Staging opl.cpp:67-80
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

        // Remaining 9 operators used for 2-op FM synthesis
        // Mirrors DOSBox Staging opl.cpp:84-96
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
    ///     Registers this device for the OPL3 and AdLib Gold I/O port ranges.
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
    ///     Releases resources held by the OPL3 FM device.
    /// </summary>
    /// <param name="disposing">True when called from <see cref="Dispose()" />; false during finalization.</param>
    private void Dispose(bool disposing) {
        if (_disposed) {
            return;
        }

        if (disposing) {
            if (_oplTimerScheduled) {
                _scheduler.RemoveEvents(_oplTimerHandler);
                _oplTimerScheduled = false;
            }

            if (_useOplIrq) {
                _dualPic.DeactivateIrq(_oplIrqLine);
            }

            _adLibGold?.Dispose();
        }

        _disposed = true;
    }

    /// <inheritdoc />
    /// <summary>
    ///     Reads from OPL3 or AdLib Gold I/O ports.
    ///     Mirrors DOSBox Staging Opl::PortRead() from opl.cpp:711-785
    /// </summary>
    public override byte ReadByte(ushort port) {
        lock (_chipLock) {
            return port switch {
                IOplPort.PrimaryAddressPortNumber => _oplIo.ReadPort(port),
                IOplPort.PrimaryDataPortNumber => _oplIo.ReadPort(port),
                IOplPort.SecondaryAddressPortNumber => _oplIo.ReadPort(port),
                IOplPort.SecondaryDataPortNumber => _oplIo.ReadPort(port),
                IOplPort.AdLibGoldAddressPortNumber when _adLibGoldIo is not null => _oplIo.ReadPort(port),
                IOplPort.AdLibGoldDataPortNumber when _adLibGoldIo is not null => _oplIo.ReadPort(port),
                _ => 0xFF
            };
        }
    }

    /// <inheritdoc />
    public override ushort ReadWord(ushort port) {
        return ReadByte(port);
    }

    /// <inheritdoc />
    /// <summary>
    ///     Writes to OPL3 or AdLib Gold I/O ports.
    ///     Mirrors DOSBox Staging Opl::PortWrite() from opl.cpp:570-709
    /// </summary>
    public override void WriteByte(ushort port, byte value) {
        Opl3WriteResult result = Opl3WriteResult.None;

        // Render cycle-accurate frames up to current time before processing write
        // Mirrors DOSBox Staging opl.cpp:573 (PortWrite calls RenderUpToNow)
        // RenderUpToNow at opl.cpp:417-432 generates frames based on elapsed time
        // and queues them in FIFO for later consumption by AudioCallback
        RenderUpToNow();
        
        // Wake up the channel on any port write
        // Mirrors DOSBox Staging opl.cpp:423 (RenderUpToNow calls channel->WakeUp())
        // This ensures the channel is enabled when OPL receives data
        _mixerChannel.WakeUp();

        switch (port) {
            case IOplPort.PrimaryAddressPortNumber:
            case IOplPort.SecondaryAddressPortNumber:
            case IOplPort.PrimaryDataPortNumber:
            case IOplPort.SecondaryDataPortNumber:
                lock (_chipLock) {
                    result = _oplIo.WritePort(port, value);
                }

                break;
            case IOplPort.AdLibGoldAddressPortNumber:
            case IOplPort.AdLibGoldDataPortNumber:
                if (_adLibGoldIo is not null) {
                    lock (_chipLock) {
                        result = _oplIo.WritePort(port, value);
                    }
                }

                break;
            default:
                LogUnhandledPortWrite(port, value);
                return;
        }

        bool timerWrite = (result & Opl3WriteResult.TimerUpdated) != 0;

        // Only schedule timer events - audio writes are handled by the mixer callback
        // The OPL chip automatically flushes buffered writes during GenerateStream
        // Mirrors DOSBox Staging: no separate flush scheduling needed
        if (timerWrite) {
            double now = _clock.ElapsedTimeMs;
            ScheduleOplTimer(now);
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
    ///     OPL3 mixer handler - called by the mixer thread to generate frames.
    ///     Mirrors DOSBox Staging Opl::AudioCallback() from opl.cpp:434-460
    /// </summary>
    public void AudioCallback(int framesRequested) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("OPL3: AudioCallback framesRequested={Frames}, FIFO size={FifoSize}", 
                framesRequested, _fifo.Count);
        }
        
        lock (_chipLock) {
            int framesRemaining = framesRequested;
            Span<float> frameData = stackalloc float[2];
            
            // First, drain any cycle-accurate frames we've queued in the FIFO
            // Mirrors DOSBox Staging opl.cpp:448-451
            while (framesRemaining > 0 && _fifo.Count > 0) {
                AudioFrame frame = _fifo.Dequeue();
                frameData[0] = frame.Left;
                frameData[1] = frame.Right;
                _mixerChannel.AddSamples_sfloat(1, frameData);
                framesRemaining--;
            }
            
            // If the FIFO ran dry, generate the remaining frames and sync up our time datum
            // Mirrors DOSBox Staging opl.cpp:454-459
            if (framesRemaining > 0) {
                GenerateFramesBatch(framesRemaining);
                
                // Sync time datum to current atomic time
                // Mirrors DOSBox Staging opl.cpp:459
                _lastRenderedMs = _clock.ElapsedTimeMs;
            }
        }

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("OPL3: AudioCallback completed, FIFO size now={FifoSize}", _fifo.Count);
        }
    }
    
    /// <summary>
    ///     Renders cycle-accurate OPL frames up to the current emulated time.
    ///     Called on every port write to maintain synchronization between CPU and audio.
    ///     Mirrors DOSBox Staging Opl::RenderUpToNow() from opl.cpp:417-432
    /// </summary>
    private void RenderUpToNow() {
        double now = _clock.ElapsedTimeMs;
        
        // Wake up the channel if it was sleeping
        // Mirrors DOSBox Staging opl.cpp:423
        if (_mixerChannel.WakeUp()) {
            _lastRenderedMs = now;
            return;
        }
        
        // Keep rendering frames until we're caught up to current time
        // Mirrors DOSBox Staging opl.cpp:428-431
        while (_lastRenderedMs < now) {
            _lastRenderedMs += _msPerFrame;
            AudioFrame frame = RenderSingleFrame();
            _fifo.Enqueue(frame);
        }
    }
    
    /// <summary>
    ///     Renders a single OPL audio frame.
    ///     Mirrors DOSBox Staging Opl::RenderFrame() from opl.cpp:380-414
    /// </summary>
    private AudioFrame RenderSingleFrame() {
        // Generate one frame (2 samples for stereo)
        Span<short> buf = stackalloc short[2];
        
        // Flush any pending OPL writes before generating audio
        double now = _clock.ElapsedTimeMs;
        _oplIo.FlushDueWritesUpTo(now);
        
        // Generate audio samples
        // Mirrors DOSBox Staging opl.cpp:399 (OPL3_GenerateStream)
        _chip.GenerateStream(buf);
        
        AudioFrame frame;
        
        // Apply AdLib Gold filtering if enabled
        // Mirrors DOSBox Staging opl.cpp:407-412
        if (_adLibGold is not null) {
            Span<float> floatBuf = stackalloc float[2];
            _adLibGold.Process(buf, 1, floatBuf);
            frame = new AudioFrame { Left = floatBuf[0], Right = floatBuf[1] };
        } else {
            // Convert int16 to float directly (no normalization)
            // Mirrors DOSBox Staging opl.cpp:410-411
            frame = new AudioFrame { Left = (float)buf[0], Right = (float)buf[1] };
        }
        
        return frame;
    }
    
    /// <summary>
    ///     Generates a batch of OPL frames directly (used when FIFO is empty).
    ///     Mirrors the frame generation logic from DOSBox Staging opl.cpp:454-458
    /// </summary>
    private void GenerateFramesBatch(int frameCount) {
        int framesGenerated = 0;
        while (framesGenerated < frameCount) {
            // Generate up to MaxSamplesPerGenerationBatch frames at a time
            int framesToGenerate = Math.Min(MaxSamplesPerGenerationBatch, frameCount - framesGenerated);
            int samplesToGenerate = framesToGenerate * 2; // stereo = 2 samples per frame
            
            if (samplesToGenerate > _tmpInterleaved.Length) {
                samplesToGenerate = _tmpInterleaved.Length;
                framesToGenerate = samplesToGenerate / 2;
            }

            Span<short> interleaved = _tmpInterleaved.AsSpan(0, samplesToGenerate);

            // Flush any pending OPL writes up to current time before generating audio
            // Mirrors DOSBox Staging opl.cpp:572-573 (RenderUpToNow pattern)
            double now = _clock.ElapsedTimeMs;
            _oplIo.FlushDueWritesUpTo(now);
            
            // Generate audio samples
            // Mirrors DOSBox Staging opl.cpp:455 (RenderFrame call) and opl.cpp:399 (OPL3_GenerateStream)
            _chip.GenerateStream(interleaved);

            // Apply AdLib Gold filtering if enabled (before float conversion)
            // Mirrors DOSBox Staging opl.cpp:407-412 (RenderFrame with adlib_gold->Process)
            if (_adLibGold is not null) {
                // Process int16 samples through AdLib Gold surround and stereo stages
                // Mirrors DOSBox Staging adlib_gold.cpp:335-358 (AdlibGold::Process method)
                Span<float> floatBuffer = _playBuffer.AsSpan(0, samplesToGenerate);
                _adLibGold.Process(interleaved, framesToGenerate, floatBuffer);
                
                // AdLib Gold.Process already outputs normalized floats
                // Mirrors DOSBox Staging opl.cpp:449 (AddSamples_sfloat call)
                _mixerChannel.AddSamples_sfloat(framesToGenerate, floatBuffer);
            } else {
                // Convert interleaved int16 samples directly to int16-ranged floats
                // Mirrors DOSBox Staging opl.cpp:410-411 (frame.left/right = buf[0]/buf[1])
                // DOSBox does NOT normalize - just casts int16 to float
                Span<float> floatBuffer = _playBuffer.AsSpan(0, samplesToGenerate);
                for (int i = 0; i < samplesToGenerate; i++) {
                    floatBuffer[i] = (float)interleaved[i]; // Keep in int16 range, no normalization
                }

                // Mirrors DOSBox Staging opl.cpp:456 (AddSamples_sfloat call in AudioCallback)
                _mixerChannel.AddSamples_sfloat(framesToGenerate, floatBuffer);
            }

            framesGenerated += framesToGenerate;
        }
    }

    /// <summary>
    ///     Schedules servicing of the OPL timers based on the next overflow.
    ///     Timer management is handled by Opl3Io which wraps OplChip timer logic.
    ///     Mirrors DOSBox Staging OplChip timer handling from opl.cpp:99-227
    /// </summary>
    /// <param name="currentTick">Current time in scheduler ticks.</param>
    private void ScheduleOplTimer(double currentTick) {
        double? delay;
        lock (_chipLock) {
            _oplIo.AdvanceTimersOnly(currentTick);
            delay = _oplIo.GetTicksUntilTimerOverflow(currentTick);
        }

        if (_oplTimerScheduled) {
            _scheduler.RemoveEvents(_oplTimerHandler);
            _oplTimerScheduled = false;
        }

        if (delay is not { } d) {
            return;
        }

        if (d < 0) {
            d = 0;
        }

        _scheduler.AddEvent(_oplTimerHandler, d);
        _oplTimerScheduled = true;
    }

    /// <summary>
    ///     Advances OPL timers to the current time and schedules the next timer event.
    ///     Mirrors DOSBox Staging Timer::Update() logic from opl.cpp:107-121
    /// </summary>
    /// <param name="unusedTick">Unused parameter supplied by the EmulationLoopScheduler event system.</param>
    private void ServiceOplTimers(uint unusedTick) {
        _ = unusedTick;
        double now = _clock.ElapsedTimeMs;
        double? delay;

        lock (_chipLock) {
            _oplIo.AdvanceTimersOnly(now);
            delay = _oplIo.GetTicksUntilTimerOverflow(now);
        }

        _oplTimerScheduled = false;

        if (delay is not { } d) {
            return;
        }

        if (d < 0) {
            d = 0;
        }

        _scheduler.AddEvent(_oplTimerHandler, d);
        _oplTimerScheduled = true;
    }

    /// <summary>
    ///     Handles changes in the OPL IRQ line state.
    ///     OPL timers can trigger IRQs when enabled (not default behavior).
    ///     Mirrors DOSBox Staging timer overflow handling from opl.cpp:229-242 (OplChip::Read)
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
