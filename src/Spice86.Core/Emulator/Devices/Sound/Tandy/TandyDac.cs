// SPDX-FileCopyrightText: 2019-2025 The DOSBox Staging Team
// SPDX-FileCopyrightText: 2002-2018 The DOSBox Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.Devices.Sound.Tandy;

using Microsoft.Extensions.Logging;

using Spice86.Audio.Backend;
using Spice86.Audio.Common;
using Spice86.Audio.Filters;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.DeviceScheduler;

using SchedulerEventHandler = Spice86.Core.Emulator.VM.DeviceScheduler.EventHandler;

/// <summary>Tandy C4h-C7h DAC with DMA 1, IRQ 7 and emulated-time production.</summary>
public sealed class TandyDac : DefaultIOPortHandler, IAudioQueueDevice<byte>, IMixerQueueNotifier, IDisposable {
    /// <summary>Native DAC base port.</summary>
    public const ushort BasePort = 0xc4;
    /// <summary>Native DAC interrupt line.</summary>
    public const byte Irq = 7;
    /// <summary>Native DAC DMA channel.</summary>
    public const byte Dma = 1;
    private readonly IOPortDispatcher _ports;
    private readonly DmaBus _dmaBus;
    private readonly DualPic _pic;
    private readonly SoftwareMixer _mixer;
    private readonly DeviceScheduler _scheduler;
    private readonly SchedulerEventHandler _tickHandler;
    private readonly byte[] _dmaBuffer = new byte[128];
    private readonly TandySoundDevice _owner;
    private readonly DmaChannel _dmaChannel;
    private ushort _clockDivider;
    private byte _mode;
    private byte _control;
    private byte _amplitude;
    private bool _dmaDone;
    private bool _playbackStarted;
    private bool _ownsDma;
    private bool _disposed;
    private float _frameCounter;

    /// <inheritdoc />
    public RWQueue<byte> OutputQueue { get; } = new(1);
    /// <inheritdoc />
    public SoundChannel Channel { get; }
    /// <summary>Whether the native DAC still owns its ports and DMA reservation.</summary>
    public bool IsAvailable => !_disposed;

    /// <summary>Installs the native DAC and reserves primary DMA 1.</summary>
    public TandyDac(State state, IOPortDispatcher ports, DmaBus dmaBus, DualPic pic,
        SoftwareMixer mixer, DeviceScheduler scheduler, IEmulatedClock clock,
        string filter, TandySoundDevice owner, ILogger logger) : base(state, false, logger) {
        _ports = ports;
        _dmaBus = dmaBus;
        _pic = pic;
        _mixer = mixer;
        _scheduler = scheduler;
        _owner = owner;
        _tickHandler = Tick;
        _dmaChannel = dmaBus.GetChannel(Dma)
            ?? throw new InvalidOperationException("Tandy DAC requires DMA 1.");
        mixer.LockMixerThread();
        bool channelCreated = false;
        bool initialized = false;
        try {
            Channel = mixer.AddChannel(
                frames => SoftwareMixer.PullFromQueueCallback<TandyDac, byte>(frames, this),
                0, "TandyDac", [ChannelFeature.Sleep, ChannelFeature.ChorusSend,
                    ChannelFeature.ReverbSend, ChannelFeature.DigitalAudio]);
            channelCreated = true;
            mixer.RegisterQueueNotifier(this);
            Channel.ConfigureEmulatedSleepClock(clock);
            Channel.SetZeroOrderHoldUpsamplerTargetRate(Channel.SampleRate);
            Channel.SetResampleMethod(ResampleMethod.ZeroOrderHoldAndResample);
            TandyAudioSettings.ConfigureFilter(Channel, filter, logger);
            OutputQueue.Resize((int)Math.Ceiling(Channel.FramesPerBlock * 2.0f));
            for (ushort port = BasePort; port < BasePort + 4; port++) {
                ports.AddIOPortHandler(port, this);
            }
            _dmaChannel.ReserveFor("Tandy DAC", OnEvicted);
            _ownsDma = true;
            scheduler.AddEvent(_tickHandler, 1);
            initialized = true;
        } finally {
            if (channelCreated && !initialized) {
                Dispose();
            }
            mixer.UnlockMixerThread();
        }
    }

    /// <inheritdoc />
    public override byte ReadByte(ushort port) => port switch {
        BasePort => (byte)(_mode & 0x77),
        BasePort + 2 => (byte)(_clockDivider & 0xff),
        BasePort + 3 => (byte)(((_clockDivider >> 8) & 15) | (_amplitude << 5)),
        _ => 0xff
    };

    /// <inheritdoc />
    public override ushort ReadWord(ushort port) =>
        (ushort)(_ports.ReadByte(port) | (_ports.ReadByte(unchecked((ushort)(port + 1))) << 8));

    /// <inheritdoc />
    public override uint ReadDWord(ushort port) =>
        ReadWord(port) | ((uint)ReadWord(unchecked((ushort)(port + 2))) << 16);

    /// <inheritdoc />
    public override void WriteWord(ushort port, ushort value) {
        _ports.WriteByte(port, (byte)(value & 0xff));
        _ports.WriteByte(unchecked((ushort)(port + 1)), (byte)(value >> 8));
    }

    /// <inheritdoc />
    public override void WriteDWord(ushort port, uint value) {
        WriteWord(port, (ushort)(value & 0xffff));
        WriteWord(unchecked((ushort)(port + 2)), (ushort)(value >> 16));
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        if (_disposed) {
            return;
        }
        byte previousMode = _mode;
        switch (port) {
            case BasePort:
                _mode = value;
                if ((value & 3) != (previousMode & 3)) {
                    ChangeMode();
                }
                if ((value & 0x0c) == 0x0c && (previousMode & 0x0c) != 0x0c) {
                    ChangeMode();
                }
                break;
            case BasePort + 1:
                if ((_mode & 3) == 1) {
                    _control = value;
                }
                break;
            case BasePort + 2:
                _clockDivider = (ushort)((_clockDivider & 0xf00) | value);
                if ((_mode & 3) != 0) {
                    ChangeMode();
                }
                break;
            case BasePort + 3:
                _clockDivider = (ushort)((_clockDivider & 0xff) | ((value & 15) << 8));
                _amplitude = (byte)(value >> 5);
                if ((_mode & 3) != 0) {
                    ChangeMode();
                }
                break;
        }
    }

    private void ChangeMode() {
        // Recording, joystick and direct-output modes are unimplemented upstream.
        if ((_mode & 3) != 3 || _clockDivider == 0) {
            return;
        }
        int rate = Ncr8496.SourceClockHz / _clockDivider;
        if (rate >= 49000) {
            return;
        }
        Channel.SampleRate = rate;
        float volume = _amplitude / 7.0f;
        Channel.AppVolume = new AudioFrame(volume, volume);
        if ((_mode & 0x0c) != 0x0c) {
            return;
        }
        _dmaDone = false;
        DmaChannel? channel = _dmaBus.GetChannel(Dma);
        if (channel is not null) {
            channel.RegisterCallback(DmaCallback);
            _playbackStarted = true;
            Channel.WakeUp();
        }
    }

    private void DmaCallback(DmaChannel channel, DmaChannel.DmaEvent dmaEvent) {
        if (dmaEvent == DmaChannel.DmaEvent.ReachedTerminalCount && !_disposed) {
            _dmaDone = true;
            _pic.ActivateIrq(Irq);
        }
    }

    private void Tick(uint value) {
        if (_disposed) {
            return;
        }
        // DMA completion must remain tied to emulated time, not host mixer sleep.
        bool active = _playbackStarted && (_mode & 0x0c) == 0x0c && !_dmaDone;
        if (Channel.IsEnabled || active) {
            _frameCounter += Channel.FramesPerTick;
            int requested = (int)Math.Floor(_frameCounter);
            _frameCounter -= requested;
            ProduceSamples(requested);
        }
        _scheduler.AddEvent(_tickHandler, 1);
    }

    private void ProduceSamples(int requested) {
        bool shouldRead = (_mode & 0x0c) == 0x0c && !_dmaDone;
        while (requested > 0) {
            int count = Math.Min(requested, _dmaBuffer.Length);
            int actual = shouldRead ? _dmaChannel.Read(count, _dmaBuffer) : 0;
            if (actual > 0 && actual < count) {
                actual--;
            }
            _dmaBuffer.AsSpan(actual, count - actual).Fill(128);
            OutputQueue.NonblockingBulkEnqueue(_dmaBuffer.AsSpan(0, count), count);
            requested -= count;
        }
    }

    private void OnEvicted() {
        Dispose();
        _owner.OnDacEvicted();
    }

    /// <inheritdoc />
    public void NotifyLockMixer() => OutputQueue.Stop();
    /// <inheritdoc />
    public void NotifyUnlockMixer() {
        if (!_disposed) {
            OutputQueue.Start();
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed) {
            return;
        }
        _mixer.LockMixerThread();
        try {
            _disposed = true;
            _scheduler.RemoveEvents(_tickHandler);
            Channel.Enable(false);
            for (ushort port = BasePort; port < BasePort + 4; port++) {
                _ports.RemoveIOPortHandler(port, this);
            }
            if (_ownsDma) {
                _dmaChannel.Reset();
                _ownsDma = false;
            }
            _pic.DeactivateIrq(Irq);
            _mixer.DeregisterChannel(Channel);
            _mixer.UnregisterQueueNotifier(this);
        } finally {
            _mixer.UnlockMixerThread();
        }
    }
}