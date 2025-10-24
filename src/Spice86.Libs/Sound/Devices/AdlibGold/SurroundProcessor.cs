// SPDX-FileCopyrightText: 2022-2025 The DOSBox Staging Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Libs.Sound.Devices.AdlibGold;

using Serilog;

using Spice86.Libs.Sound.Common;
using Spice86.Libs.Sound.Devices.YM7128B;

/// <summary>
///     Provides surround processing emulation for the AdLib Gold optional module.
/// </summary>
internal sealed class SurroundProcessor : IDisposable {
    private readonly Ym7128BChip _chip = new();
    private readonly ILogger _logger;
    private readonly Ym7128BChipIdealProcessData _processData = new();

    private ControlState _control;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SurroundProcessor" /> class.
    /// </summary>
    /// <param name="sampleRateHz">The sample rate used by the mixer.</param>
    /// <param name="logger">Logger used to trace register activity.</param>
    internal SurroundProcessor(int sampleRateHz, ILogger logger) {
        _logger = logger.ForContext<SurroundProcessor>();

        if (sampleRateHz < 10) {
            _logger.Error(
                "The surround processor requires a sample rate of at least 10 Hz. Provided value: {SampleRateHz}",
                sampleRateHz);
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz), "Sample rate must be at least 10 Hz.");
        }

        _chip.IdealSetup((nuint)sampleRateHz);
        _chip.IdealReset();
        _chip.IdealStart();

        _logger.Debug("Surround processor initialized at sample rate {SampleRateHz}", sampleRateHz);
    }

    /// <summary>
    ///     Releases the unmanaged resources owned by the surround chip.
    /// </summary>
    public void Dispose() {
        _logger.Debug("Disposing surround processor.");

        _chip.IdealStop();
        _chip.IdealDtor();
    }

    /// <summary>
    ///     Processes a serialized control value written to the surround module.
    /// </summary>
    /// <param name="value">The value driven on the control pins.</param>
    internal void ControlWrite(byte value) {
        var reg = new SurroundControlReg(value);

        if (_control.A0 != 0 && reg.A0 == 0) {
            _logger.Verbose(
                "Writing surround register {Register} with value {Data}",
                _control.Address,
                _control.Data);

            _chip.IdealWrite(_control.Address, _control.Data);
        } else {
            if (_control.Sci == 0 && reg.Sci != 0) {
                if (reg.A0 != 0) {
                    _control.Data = (byte)((_control.Data << 1) | reg.Din);
                } else {
                    _control.Address = (byte)((_control.Address << 1) | reg.Din);
                }
            }
        }

        _control.Sci = reg.Sci;
        _control.A0 = reg.A0;
    }

    /// <summary>
    ///     Processes a stereo frame through the surround module.
    /// </summary>
    /// <param name="frame">Input frame containing interleaved left and right samples.</param>
    /// <returns>The surround-processed frame.</returns>
    internal AudioFrame Process(AudioFrame frame) {
        _processData.Inputs[0] = frame.Left + frame.Right;
        _chip.IdealProcess(_processData);
        return new AudioFrame(_processData.Outputs[0], _processData.Outputs[1]);
    }

    /// <summary>
    ///     Represents the serialized control register view for the surround processor.
    /// </summary>
    private readonly struct SurroundControlReg(byte data) {
        /// <summary>
        ///     Gets the data bit written to the DIN pin.
        /// </summary>
        internal byte Din => (byte)(data & 0x01);

        /// <summary>
        ///     Gets the state of the serial clock input.
        /// </summary>
        internal byte Sci => (byte)((data >> 1) & 0x01);

        /// <summary>
        ///     Gets the state of the word clock input.
        /// </summary>
        internal byte A0 => (byte)((data >> 2) & 0x01);
    }

    /// <summary>
    ///     Stores intermediate state while the serialized control word is assembled.
    /// </summary>
    private struct ControlState {
        /// <summary>
        ///     Tracks the previous serial clock state.
        /// </summary>
        internal byte Sci;

        /// <summary>
        ///     Tracks the previous word clock state.
        /// </summary>
        internal byte A0;

        /// <summary>
        ///     Tracks the latched register address.
        /// </summary>
        internal byte Address;

        /// <summary>
        ///     Tracks the latched register data.
        /// </summary>
        internal byte Data;
    }
}