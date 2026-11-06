// Tandy register dispatch adapted from DOSBox Staging.
// SPDX-FileCopyrightText: 2020-2026 The DOSBox Staging Team
// SPDX-FileCopyrightText: 2002-2021 The DOSBox Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.Devices.Video;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Clock;

using System.Linq;

/// <summary>Handles the Tandy gate array and mirrored CRTC ports.</summary>
public sealed class TandyIoPortHandler : VgaIoPortHandler {
    private readonly TandyVideoState _tandy;
    private readonly IVideoState _videoState;
    private readonly IEmulatedClock _clock;

    /// <summary>Registers the native Tandy video ports without VGA extensions.</summary>
    public TandyIoPortHandler(State state, IOPortDispatcher ioPortDispatcher, ILogger loggerService,
        IVideoState videoState, TandyVideoState tandy, IEmulatedClock clock, bool failOnUnhandledPort)
        : base(state, ioPortDispatcher, loggerService, videoState, clock, failOnUnhandledPort,
            Enumerable.Range(0x3D0, 13).Append(0x3DE).Append(0x3DF)) {
        _tandy = tandy;
        _videoState = videoState;
        _clock = clock;
    }

    /// <inheritdoc />
    public override byte ReadByte(ushort port) {
        if (port == 0x3DA) {
            return (byte)((_videoState.BeamTiming.ReadStatus(_clock.ElapsedTimeMs) & 9) | 4);
        }
        if (port is >= 0x3D0 and <= 0x3D7) {
            if ((port & 1) == 0) {
                return _tandy.CrtcIndex;
            }
            return _tandy.CrtcIndex < 32 ? _tandy.Crtc[_tandy.CrtcIndex] : (byte)0xFF;
        }
        return 0xFF;
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        _videoState.IsRenderingDirty = true;
        if (port is >= 0x3D0 and <= 0x3D7) {
            if ((port & 1) == 0) {
                _tandy.CrtcIndex = value;
            } else {
                _tandy.WriteCrtc(value);
            }
        } else {
            switch (port) {
                case 0x3D8:
                    _tandy.WriteMode(value);
                    break;
                case 0x3D9:
                    _tandy.ColorSelect = value;
                    break;
                case 0x3DA:
                    _tandy.RegisterIndex = value;
                    break;
                case 0x3DB:
                    _tandy.LightPenTriggered = false;
                    break;
                case 0x3DC:
                    LatchLightPen();
                    break;
                case 0x3DE:
                    _tandy.WriteRegister(value);
                    break;
                case 0x3DF:
                    _tandy.WritePageBank(value);
                    break;
            }
        }
        _videoState.BeamTiming.Refresh(_clock.ElapsedTimeMs);
    }

    private void LatchLightPen() {
        if (_tandy.LightPenTriggered) {
            return;
        }
        _tandy.LightPenTriggered = true;
        TandyVideoState.ScanoutParameters scanout = _tandy.Scanout;
        int addressAdd = scanout.Width / 4;
        if (scanout.IsGraphics) {
            addressAdd = scanout.Width * scanout.BitsPerPixel / 8;
        }
        ushort address = _videoState.BeamTiming.GetLightPenAddress(_clock.ElapsedTimeMs, addressAdd);
        _tandy.Crtc[0x10] = (byte)(address >> 8);
        _tandy.Crtc[0x11] = (byte)address;
    }
}