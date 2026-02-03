// SPDX-FileCopyrightText: 2022-2025 The DOSBox Staging Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Libs.Sound.Devices.AdlibGold;

using Serilog;

using Spice86.Libs.Sound.Common;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
///     AdLib Gold module providing surround and stereo processing.
///     Reference: class AdlibGold in DOSBox adlib_gold.h/adlib_gold.cpp
/// </summary>
public sealed class AdlibGold : IDisposable {
    private readonly SurroundProcessor _surroundProcessor;
    private readonly StereoProcessor _stereoProcessor;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AdlibGold" /> class.
    ///     Reference: AdlibGold::AdlibGold(const int sample_rate_hz) in DOSBox
    /// </summary>
    /// <param name="sampleRateHz">The sample rate used for audio processing.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public AdlibGold(int sampleRateHz, ILogger logger) {
        _surroundProcessor = new SurroundProcessor(sampleRateHz, logger);
        _stereoProcessor = new StereoProcessor(sampleRateHz, logger);
    }

    /// <summary>
    ///     Releases resources held by the AdLib Gold module.
    ///     Reference: AdlibGold::~AdlibGold() in DOSBox
    /// </summary>
    public void Dispose() {
        _surroundProcessor.Dispose();
    }

    /// <summary>
    ///     Writes to the stereo processor control register.
    ///     Reference: AdlibGold::StereoControlWrite() in DOSBox
    /// </summary>
    /// <param name="reg">The stereo processor control register.</param>
    /// <param name="data">The value to write.</param>
    public void StereoControlWrite(StereoProcessorControlReg reg, byte data) {
        _stereoProcessor.ControlWrite(reg, data);
    }

    /// <summary>
    ///     Writes to the surround processor control register.
    ///     Reference: AdlibGold::SurroundControlWrite() in DOSBox
    /// </summary>
    /// <param name="val">The control value to write.</param>
    public void SurroundControlWrite(byte val) {
        _surroundProcessor.ControlWrite(val);
    }

    /// <summary>
    ///     Processes audio frames through the surround and stereo processors.
    ///     Reference: AdlibGold::Process() in DOSBox
    /// </summary>
    /// <param name="input">Interleaved 16-bit PCM input.</param>
    /// <param name="frames">Number of stereo frames to process.</param>
    /// <param name="output">Output buffer for processed floating-point samples.</param>
    public void Process(ReadOnlySpan<short> input, int frames, Span<float> output) {
        int samples = frames * 2;
        ref short inputRef = ref MemoryMarshal.GetReference(input);
        ref float outputRef = ref MemoryMarshal.GetReference(output);

        // Additional wet signal level boost to make the emulated
        // sound more closely resemble real hardware recordings.
        // Reference: constexpr auto wet_boost = 1.8f in DOSBox
        const float wetBoost = 1.8f;

        for (int sampleIndex = 0; sampleIndex < samples; sampleIndex += 2) {
            short left16 = Unsafe.Add(ref inputRef, sampleIndex);
            short right16 = Unsafe.Add(ref inputRef, sampleIndex + 1);
            var frame = new AudioFrame(left16, right16);

            AudioFrame wet = _surroundProcessor.Process(frame);

            frame.Left += wet.Left * wetBoost;
            frame.Right += wet.Right * wetBoost;

            _stereoProcessor.Process(ref frame);

            Unsafe.Add(ref outputRef, sampleIndex) = frame.Left;
            Unsafe.Add(ref outputRef, sampleIndex + 1) = frame.Right;
        }
    }
}
