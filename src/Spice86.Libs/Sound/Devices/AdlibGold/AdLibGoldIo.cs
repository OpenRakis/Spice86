// SPDX-FileCopyrightText: 2022-2025 The DOSBox Staging Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Libs.Sound.Devices.AdlibGold;

using Serilog;
using Serilog.Events;

using Spice86.Libs.Sound.Devices.NukedOpl3;

/// <summary>
///     Handles AdLib Gold-specific register access and mixer integration.
/// </summary>
public sealed class AdLibGoldIo {
    private const byte DefaultVolume = 0xff;
    private readonly AdLibGoldDevice _device;
    private readonly ILogger _logger;
    private readonly Action<float, float>? _volumeHandler;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AdLibGoldIo" /> class.
    /// </summary>
    /// <param name="device">The owning AdLib Gold processing chain.</param>
    /// <param name="volumeHandler">Optional callback invoked when mixer volume values change.</param>
    /// <param name="logger">Logger used to record register activity.</param>
    internal AdLibGoldIo(AdLibGoldDevice device, Action<float, float>? volumeHandler, ILogger logger) {
        _device = device;
        _volumeHandler = volumeHandler;
        _logger = logger.ForContext<AdLibGoldIo>();

        _logger.Debug("AdLib Gold I/O helper created. Mixer callback provided: {HasVolumeHandler}",
            volumeHandler is not null);
    }

    /// <summary>
    ///     Gets or sets the register index selected through the AdLib Gold I/O ports.
    /// </summary>
    internal byte Index { get; set; }

    /// <summary>
    ///     Gets the last programmed left channel mixer volume.
    /// </summary>
    private byte LeftVolume { get; set; } = DefaultVolume;

    /// <summary>
    ///     Gets the last programmed right channel mixer volume.
    /// </summary>
    private byte RightVolume { get; set; } = DefaultVolume;

    /// <summary>
    ///     Gets or sets a value indicating whether the virtual board is active.
    /// </summary>
    internal bool Active { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether volume changes should be forwarded to the host mixer.
    /// </summary>
    internal bool MixerEnabled { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether DC bias removal should be enabled for PCM playback.
    ///     When enabled, a moving-average filter subtracts the long-term offset that some titles introduce
    ///     when they stream samples through the OPL path, which otherwise manifests as pops or low hum after
    ///     playback stops. This should be treated as a compatibility safeguard because it slightly trims
    ///     very-low-frequency content while correcting the offset.
    /// </summary>
    internal bool WantsDcBiasRemoved { get; set; }

    /// <summary>
    ///     Handles a write to the currently selected register.
    /// </summary>
    /// <param name="value">Value written through the AdLib Gold data port.</param>
    internal void Write(byte value) {
        switch (Index) {
            case 0x04:
                _device.StereoControlWrite(StereoProcessorControlReg.VolumeLeft, value);
                break;
            case 0x05:
                _device.StereoControlWrite(StereoProcessorControlReg.VolumeRight, value);
                break;
            case 0x06:
                _device.StereoControlWrite(StereoProcessorControlReg.Bass, value);
                break;
            case 0x07:
                _device.StereoControlWrite(StereoProcessorControlReg.Treble, value);
                break;
            case 0x08:
                _device.StereoControlWrite(StereoProcessorControlReg.SwitchFunctions, value);
                break;
            case 0x09:
                LeftVolume = value;
                ApplyVolume();
                break;
            case 0x0a:
                RightVolume = value;
                ApplyVolume();
                break;
            case 0x18:
                _device.SurroundControlWrite(value);
                break;
            default:
                _logger.Debug("Unhandled AdLib Gold register write to index {Register:X2} with value {Value:X2}", Index,
                    value);
                break;
        }
    }

    /// <summary>
    ///     Reads from the currently selected register.
    /// </summary>
    /// <returns>The value returned by the virtual register.</returns>
    internal byte Read() {
        byte result = Index switch {
            0x00 => 0x50, // Board options: 16-bit ISA, surround module, no telephone/CD-ROM
            0x09 => LeftVolume,
            0x0a => RightVolume,
            0x15 => IOplPort.PrimaryAddressPortNumber >> 3,
            _ => 0xff
        };

        if (result == 0xff && _logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose(
                "Read from unhandled AdLib Gold register index {Register:X2}, returning {Result:X2}",
                Index,
                result);
        }

        return result;
    }

    /// <summary>
    ///     Applies the current left and right mixer values to the host callback.
    /// </summary>
    private void ApplyVolume() {
        if (!MixerEnabled) {
            _logger.Verbose("Ignoring volume write because mixer control is disabled.");
            return;
        }

        if (_volumeHandler == null) {
            _logger.Warning(
                "Mixer volume update skipped because no callback was supplied. Left: {LeftVolume:X2}, Right: {RightVolume:X2}",
                LeftVolume, RightVolume);
            return;
        }

        const int mask = 0x1f;
        const float denominator = 31.0f;
        float left = (LeftVolume & mask) / denominator;
        float right = (RightVolume & mask) / denominator;

        _logger.Debug("Applying AdLib Gold mixer levels. Left: {LeftLevel}, Right: {RightLevel}", left, right);
        _volumeHandler(left, right);
    }
}