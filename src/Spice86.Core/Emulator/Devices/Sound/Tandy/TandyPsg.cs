// SPDX-FileCopyrightText: 2019-2025 The DOSBox Staging Team
// SPDX-FileCopyrightText: 2002-2018 The DOSBox Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.Devices.Sound.Tandy;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Clock;

/// <summary>Native Tandy PSG ports, cycle-accurate writes and mixer integration.</summary>
public sealed class TandyPsg : DefaultIOPortHandler, IDisposable {
    private readonly Ncr8496 _chip = new();
    private readonly Lock _mutex = new();
    private readonly Queue<float> _fifo = new();
    private readonly IOPortDispatcher _ports;
    private readonly SoftwareMixer _mixer;
    private readonly IEmulatedClock _clock;
    private readonly SoundChannel _channel;
    private float[] _renderBuffer = [];
    private double _lastRenderedMs;
    private bool _disposed;

    /// <summary>Installs the native NCR8496 at C0h-C1h.</summary>
    public TandyPsg(State state, IOPortDispatcher ports, SoftwareMixer mixer,
        IEmulatedClock clock, string fadeout, string filter, ILogger logger)
        : base(state, false, logger) {
        _ports = ports;
        _mixer = mixer;
        _clock = clock;
        _lastRenderedMs = clock.ElapsedTimeMs;
        mixer.LockMixerThread();
        bool channelCreated = false;
        bool initialized = false;
        try {
            _channel = mixer.AddChannel(AudioCallback, Ncr8496.RenderRateHz, "TandyPsg",
                [ChannelFeature.Sleep, ChannelFeature.FadeOut, ChannelFeature.ReverbSend,
                    ChannelFeature.ChorusSend, ChannelFeature.Synthesizer]);
            channelCreated = true;
            if (!_channel.ConfigureFadeOut(fadeout, clock)) {
                logger.LogWarning("TandyPsg: Invalid fade-out '{Fadeout}', using off", fadeout);
            }
            TandyAudioSettings.ConfigureFilter(_channel, filter, logger);
            ports.AddIOPortHandler(0xc0, this);
            ports.AddIOPortHandler(0xc1, this);
            initialized = true;
        } finally {
            if (channelCreated && !initialized) {
                Dispose();
            }
            mixer.UnlockMixerThread();
        }
    }

    /// <inheritdoc />
    public override byte ReadByte(ushort port) => 0xff;

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
        lock (_mutex) {
            if (_disposed) {
                return;
            }
            RenderUpToNow();
            _chip.Write(value);
        }
    }

    private void RenderUpToNow() {
        double now = _clock.ElapsedTimeMs;
        if (_channel.WakeUp()) {
            _lastRenderedMs = now;
            return;
        }
        const double MsPerRender = 1000.0 / Ncr8496.RenderRateHz;
        while (_lastRenderedMs < now) {
            _lastRenderedMs += MsPerRender;
            _fifo.Enqueue(_chip.RenderSample());
        }
    }

    private void AudioCallback(int requestedFrames) {
        lock (_mutex) {
            if (_renderBuffer.Length < requestedFrames) {
                _renderBuffer = new float[requestedFrames];
            }
            int index = 0;
            while (index < requestedFrames && _fifo.TryDequeue(out float sample)) {
                _renderBuffer[index++] = sample;
            }
            while (index < requestedFrames) {
                _renderBuffer[index++] = _chip.RenderSample();
            }
            _channel.AddSamplesGeneric(requestedFrames, _renderBuffer.AsSpan(0, requestedFrames), false);
            _lastRenderedMs = _clock.ElapsedTimeMs;
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed) {
            return;
        }
        _mixer.LockMixerThread();
        try {
            lock (_mutex) {
                _disposed = true;
                _channel.Enable(false);
                _fifo.Clear();
                _ports.RemoveIOPortHandler(0xc0, this);
                _ports.RemoveIOPortHandler(0xc1, this);
                _mixer.DeregisterChannel(_channel);
            }
        } finally {
            _mixer.UnlockMixerThread();
        }
    }
}