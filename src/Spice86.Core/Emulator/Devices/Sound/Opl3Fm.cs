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

/// <summary>
///     Virtual device which emulates OPL3 FM sound.
/// </summary>
public class Opl3Fm : DefaultIOPortHandler, IDisposable {
    private const int MaxSamplesPerGenerationBatch = 512;
    private readonly AdLibGoldDevice? _adLibGold;
    private readonly AdLibGoldIo? _adLibGoldIo;
    private readonly Opl3Chip _chip = new();
    private readonly object _chipLock = new();
    private readonly DeviceThread _deviceThread;
    private readonly EmulationLoopScheduler _scheduler;
    private readonly IEmulatedClock _clock;
    private readonly DualPic _dualPic;
    private readonly EventHandler _oplFlushHandler;
    private readonly Opl3Io _oplIo;
    private readonly byte _oplIrqLine;
    private readonly EventHandler _oplTimerHandler;
    private readonly float[] _playBuffer = new float[2048];

    /// <summary>
    ///     The mixer channel used for the OPL3 FM synth.
    /// </summary>
    private readonly MixerChannel _mixerChannel;

    private readonly short[] _tmpInterleaved = new short[2048];
    private readonly bool _useOplIrq;
    private bool _disposed;
    private bool _oplFlushScheduled;
    private bool _oplTimerScheduled;

    /// <summary>
    ///     Initializes a new instance of the OPL3 FM synth chip.
    /// </summary>
    /// <param name="fmSynthMixerChannel">The software mixer's channel for the OPL3 FM Synth chip.</param>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="ioPortDispatcher">
    ///     The class that is responsible for dispatching ports reads and writes to classes that
    ///     respond to them.
    /// </param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O port wasn't handled.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="pauseHandler">Class for handling pausing the emulator.</param>
    /// <param name="scheduler">The event scheduler.</param>
    /// <param name="clock">The emulated clock.</param>
    /// <param name="dualPic">The shared dual PIC scheduler.</param>
    /// <param name="useAdlibGold">True to enable AdLib Gold filtering and surround processing.</param>
    /// <param name="enableOplIrq">True to forward OPL IRQs to the PIC.</param>
    /// <param name="oplIrqLine">IRQ line used when OPL IRQs are enabled.</param>
    public Opl3Fm(MixerChannel fmSynthMixerChannel, State state,
        IOPortDispatcher ioPortDispatcher, bool failOnUnhandledPort,
        ILoggerService loggerService, IPauseHandler pauseHandler, EmulationLoopScheduler scheduler, IEmulatedClock clock, DualPic dualPic,
        bool useAdlibGold = false, bool enableOplIrq = false, byte oplIrqLine = 5)
        : base(state, failOnUnhandledPort, loggerService) {
        _mixerChannel = fmSynthMixerChannel;
        _scheduler = scheduler;
        _clock = clock;
        _dualPic = dualPic;
        bool useAdLibGold = useAdlibGold;
        _useOplIrq = enableOplIrq;
        _oplIrqLine = oplIrqLine;
        _deviceThread = new DeviceThread(nameof(Opl3Fm), PlaybackLoopBody, pauseHandler, loggerService);

        _oplFlushHandler = FlushOplWrites;
        _oplTimerHandler = ServiceOplTimers;

        _oplIo = new Opl3Io(_chip, () => _clock.CurrentTimeMs) {
            OnIrqChanged = OnOplIrqChanged
        };

        int sampleRate = _mixerChannel.GetSampleRate();
        if (useAdLibGold) {
            _adLibGold = new AdLibGoldDevice(sampleRate, loggerService);
            _adLibGoldIo = _adLibGold.CreateIoAttachedTo(_oplIo);
        }

        _loggerService.Debug(
            "Initializing OPL3 FM synth. AdLib Gold enabled: {AdLibGoldEnabled}, OPL IRQ enabled: {OplIrqEnabled}, Sample rate: {SampleRate}",
            useAdLibGold, _useOplIrq, sampleRate);

        _oplIo.Reset((uint)sampleRate);

        InitializeToneGenerators();

        InitPortHandlers(ioPortDispatcher);
    }

    /// <inheritdoc />
    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Initializes default envelopes and rates for the OPL3 operators.
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
    ///     Registers this device for the OPL3 and AdLib Gold I/O port ranges.
    /// </summary>
    /// <param name="ioPortDispatcher">Dispatcher used to route port accesses.</param>
    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(IOplPort.PrimaryAddressPortNumber, this);
        ioPortDispatcher.AddIOPortHandler(IOplPort.PrimaryDataPortNumber, this);
        ioPortDispatcher.AddIOPortHandler(IOplPort.SecondaryAddressPortNumber, this);
        ioPortDispatcher.AddIOPortHandler(IOplPort.SecondaryDataPortNumber, this);
        ioPortDispatcher.AddIOPortHandler(IOplPort.AdLibGoldAddressPortNumber, this);
        ioPortDispatcher.AddIOPortHandler(IOplPort.AdLibGoldDataPortNumber, this);
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
            if (_oplFlushScheduled) {
                _scheduler.RemoveEvents(_oplFlushHandler);
                _oplFlushScheduled = false;
            }

            if (_oplTimerScheduled) {
                _scheduler.RemoveEvents(_oplTimerHandler);
                _oplTimerScheduled = false;
            }

            if (_useOplIrq) {
                _dualPic.DeactivateIrq(_oplIrqLine);
            }

            _deviceThread.Dispose();
            _adLibGold?.Dispose();
        }

        _disposed = true;
    }

    /// <inheritdoc />
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
    public override void WriteByte(ushort port, byte value) {
        Opl3WriteResult result = Opl3WriteResult.None;

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

        bool audioWrite = (result & (Opl3WriteResult.DataWrite | Opl3WriteResult.AdLibGoldWrite)) != 0;
        bool timerWrite = (result & Opl3WriteResult.TimerUpdated) != 0;

        switch (audioWrite) {
            case true:
                InitializePlaybackIfNeeded();
                break;
            case false when !timerWrite:
                return;
        }

        double now = _clock.CurrentTimeMs;

        if (audioWrite) {
            ScheduleOplFlush(now);
        }

        if (timerWrite) {
            ScheduleOplTimer(now);
        }
    }

    /// <summary>
    ///     Starts the playback thread if it is currently idle.
    /// </summary>
    private void InitializePlaybackIfNeeded() {
        if (_deviceThread.Active) {
            return;
        }

        _loggerService.Debug("Starting OPL3 FM playback thread.");
        RenderTo(_playBuffer);
        _deviceThread.StartThreadIfNeeded();
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
    ///     Generates and plays back output waveform data.
    /// </summary>
    private void PlaybackLoopBody() {
        RenderTo(_playBuffer);
        
        _mixerChannel.AudioFrames.Clear();
        for (int i = 0; i < _playBuffer.Length; i += 2) {
            _mixerChannel.AudioFrames.Add(new AudioFrame(_playBuffer[i], _playBuffer[i + 1]));
        }
    }

    /// <summary>
    ///     Renders audio samples into the provided destination buffer.
    /// </summary>
    /// <param name="destination">Interleaved stereo output buffer that receives the generated samples.</param>
    private void RenderTo(Span<float> destination) {
        int frames = destination.Length / 2;
        if (frames <= 0) {
            destination.Clear();
            return;
        }

        int samples = frames * 2;
        if (samples > _tmpInterleaved.Length) {
            throw new ArgumentException("Destination span is larger than the temporary buffer.", nameof(destination));
        }

        Span<short> interleaved = _tmpInterleaved.AsSpan(0, samples);

        int generatedSamples = 0;
        while (generatedSamples < samples) {
            int batchSamples = Math.Min(MaxSamplesPerGenerationBatch, samples - generatedSamples);
            Span<short> batch = interleaved.Slice(generatedSamples, batchSamples);
            lock (_chipLock) {
                _chip.GenerateStream(batch);
            }

            generatedSamples += batchSamples;
        }

        const float scale = 1.0f / 32768f;

        if (_adLibGold is null) {
            SimdConversions.ConvertInt16ToScaledFloat(interleaved, destination, scale);
        } else {
            _adLibGold.Process(interleaved, frames, destination);
            SimdConversions.ScaleInPlace(destination, scale);
        }
    }

    /// <summary>
    ///     Schedules the next flush of pending OPL register writes.
    /// </summary>
    /// <param name="currentTick">Current time in scheduler ticks.</param>
    private void ScheduleOplFlush(double currentTick) {
        double? delay;
        lock (_chipLock) {
            delay = GetNextFlushDelayUnsafe(currentTick, true);
        }

        if (_oplFlushScheduled) {
            _scheduler.RemoveEvents(_oplFlushHandler);
            _oplFlushScheduled = false;
        }

        if (delay is not { } d) {
            return;
        }

        if (d < 0) {
            d = 0;
        }

        _scheduler.AddEvent(_oplFlushHandler, d);
        _oplFlushScheduled = true;
    }

    /// <summary>
    ///     Schedules servicing of the OPL timers based on the next overflow.
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
    ///     Flushes pending writes up to the current time and schedules the next flush if required.
    /// </summary>
    /// <param name="unusedTick">Unused parameter supplied by the EmulationLoopScheduler event system.</param>
    private void FlushOplWrites(uint unusedTick) {
        double now = _clock.CurrentTimeMs;
        double? delay;

        lock (_chipLock) {
            _oplIo.FlushDueWritesUpTo(now);
            delay = GetNextFlushDelayUnsafe(now, false);
        }

        _oplFlushScheduled = false;

        if (delay is not { } d) {
            return;
        }

        if (d < 0) {
            d = 0;
        }

        _scheduler.AddEvent(_oplFlushHandler, d);
        _oplFlushScheduled = true;
    }

    /// <summary>
    ///     Advances OPL timers to the current time and schedules the next timer event.
    /// </summary>
    /// <param name="unusedTick">Unused parameter supplied by the EmulationLoopScheduler event system.</param>
    private void ServiceOplTimers(uint unusedTick) {
        _ = unusedTick;
        double now = _clock.CurrentTimeMs;
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

    private double? GetNextFlushDelayUnsafe(double currentTick, bool flushImmediately) {
        double? delay = _oplIo.GetTicksUntilNextWrite(currentTick);
        if (!flushImmediately || delay is not { } d || d > 0) {
            return delay;
        }

        _oplIo.FlushDueWritesUpTo(currentTick);
        return _oplIo.GetTicksUntilNextWrite(currentTick);
    }

    /// <summary>
    ///     Handles changes in the OPL IRQ line state.
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
