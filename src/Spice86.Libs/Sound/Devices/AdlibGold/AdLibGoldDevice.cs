namespace Spice86.Libs.Sound.Devices.AdlibGold;

using Serilog;

using Spice86.Libs.Sound.Common;
using Spice86.Libs.Sound.Devices.NukedOpl3;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
///     Provides surround and stereo processing that replicates the AdLib Gold signal chain.
/// </summary>
public sealed class AdLibGoldDevice : IDisposable {
    private readonly ILogger _logger;
    private readonly StereoProcessor _stereo;
    private readonly SurroundProcessor _surround;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AdLibGoldDevice" /> class.
    /// </summary>
    /// <param name="sampleRateHz">The mixer sample rate used for the processed audio stream.</param>
    /// <param name="logger">Logger used to track device activity.</param>
    public AdLibGoldDevice(int sampleRateHz, ILogger logger) {
        _logger = logger.ForContext<AdLibGoldDevice>();
        _stereo = new StereoProcessor(sampleRateHz, _logger);
        _surround = new SurroundProcessor(sampleRateHz, _logger);

        _logger.Debug("AdLib Gold device initialized at sample rate {SampleRateHz}", sampleRateHz);
    }

    /// <summary>
    ///     Releases managed resources held by the surround processor.
    /// </summary>
    public void Dispose() {
        _logger.Debug("Disposing AdLib Gold device.");
        _surround.Dispose();
    }

    /// <summary>
    ///     Creates an <see cref="AdLibGoldIo" /> helper and attaches it to the supplied opl I/O layer.
    /// </summary>
    /// <param name="oplIo">The opl I/O interface that exposes AdLib Gold registers.</param>
    /// <param name="volumeHandler">Optional callback invoked when the AdLib Gold mixer volume changes.</param>
    /// <returns>The configured <see cref="AdLibGoldIo" /> instance.</returns>
    public AdLibGoldIo CreateIoAttachedTo(OplIo oplIo, Action<float, float>? volumeHandler = null) {
        ArgumentNullException.ThrowIfNull(oplIo);

        var io = new AdLibGoldIo(this, volumeHandler, _logger);
        oplIo.AttachAdLibGold(io);

        _logger.Debug("AdLib Gold I/O helper attached to the opl interface.");

        return io;
    }

    /// <summary>
    ///     Forwards a raw surround control write to the surround processor.
    /// </summary>
    /// <param name="value">The control value written via the AdLib Gold serial interface.</param>
    internal void SurroundControlWrite(byte value) {
        _logger.Verbose("Surround control write value {Value:X2}", value);
        _surround.ControlWrite(value);
    }

    /// <summary>
    ///     Forwards a stereo processor register write.
    /// </summary>
    /// <param name="reg">The targeted stereo processor register.</param>
    /// <param name="value">The raw value written to the register.</param>
    internal void StereoControlWrite(StereoProcessorControlReg reg, byte value) {
        _logger.Verbose("Stereo control write to {Register} with value {Value:X2}", reg, value);
        _stereo.ControlWrite(reg, value);
    }

    /// <summary>
    ///     Processes interleaved PCM samples through the surround and stereo stages.
    /// </summary>
    /// <param name="input">Interleaved 16-bit PCM input frames.</param>
    /// <param name="frames">The number of stereo frames to transform.</param>
    /// <param name="output">Target buffer that receives interleaved floating-point frames.</param>
    public void Process(ReadOnlySpan<short> input, int frames, Span<float> output) {
        int requiredSamples = frames * 2;
        if (input.Length < requiredSamples) {
            _logger.Error(
                "Insufficient input samples. Frames requested: {Frames}, available samples: {InputLength}",
                frames,
                input.Length);
            throw new ArgumentException("Input buffer too small for requested frames.", nameof(input));
        }

        if (output.Length < requiredSamples) {
            _logger.Error(
                "Insufficient output capacity. Frames requested: {Frames}, available samples: {OutputLength}",
                frames,
                output.Length);

            throw new ArgumentException("Output buffer too small for requested frames.", nameof(output));
        }

        int samples = frames * 2;
        ref short inputRef = ref MemoryMarshal.GetReference(input);
        ref float outputRef = ref MemoryMarshal.GetReference(output);
        const float wetBoost = 1.8f;

        for (int sampleIndex = 0; sampleIndex < samples; sampleIndex += 2) {
            short left16 = Unsafe.Add(ref inputRef, sampleIndex);
            short right16 = Unsafe.Add(ref inputRef, sampleIndex + 1);
            var frame = new AudioFrame(left16, right16);
            AudioFrame wet = _surround.Process(frame);

            frame.Left += wet.Left * wetBoost;
            frame.Right += wet.Right * wetBoost;

            _stereo.Process(ref frame);

            Unsafe.Add(ref outputRef, sampleIndex) = frame.Left;
            Unsafe.Add(ref outputRef, sampleIndex + 1) = frame.Right;
        }
    }
}
