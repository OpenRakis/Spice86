namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

using Serilog.Events;

using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Buffers;
using System.Timers;

/// <summary>
/// Emulates the Sound Blaster 16 DSP.
/// </summary>
public sealed class Dsp : IDisposable {
    private readonly ADPCM2 _adpcm2;
    private readonly ADPCM3 _adpcm3;
    private readonly ADPCM4 _adpcm4;
    private readonly DmaSystem _dmaSystem;
    private readonly ILoggerService _logger;
    private readonly int _lowDmaChannelNumber;
    private readonly int? _highDmaChannelNumber;

    /// <summary>
    /// Initializes a new instance of the Digital Signal Processor.
    /// </summary>
    /// <param name="dmaSystem">DMA subsystem providing channel access.</param>
    /// <param name="lowDmaChannelNumber">Channel number used for 8-bit transfers.</param>
    /// <param name="highDmaChannelNumber">Optional channel number used for 16-bit transfers.</param>
    /// <param name="loggerService">Service instance used to log DSP activity.</param>
    public Dsp(DmaSystem dmaSystem, int lowDmaChannelNumber, int? highDmaChannelNumber, ILoggerService loggerService) {
        _adpcm2 = new ADPCM2();
        _adpcm3 = new ADPCM3();
        _adpcm4 = new ADPCM4();
        _dmaSystem = dmaSystem;
        _logger = loggerService;
        _lowDmaChannelNumber = lowDmaChannelNumber;
        _highDmaChannelNumber = highDmaChannelNumber;
        SampleRate = 22050;
        BlockTransferSize = 65536;

        _logger.Debug(
            "DSP initialized with low DMA channel {LowDma} and high DMA channel {HighDma}",
            _lowDmaChannelNumber,
            _highDmaChannelNumber);
    }

    /// <summary>
    /// Determines whether the DSP write buffer can accept additional PCM data.
    /// </summary>
    /// <returns><c>true</c> when the current state prevents new data from being queued or the buffer is full.</returns>
    public bool IsWriteBufferAtCapacity() {
        return State != DspState.Normal || IsAtCapacity;
    }

    /// <summary>
    /// Gets the current DSP state.
    /// </summary>
    public DspState State { get; private set; }

    /// <summary>
    /// Gets or sets the DSP's sample rate.
    /// </summary>
    public int SampleRate { get; set; }

    /// <summary>
    /// Gets or sets the size of a transfer block for auto-init mode.
    /// </summary>
    public int BlockTransferSize { get; set; }

    /// <summary>
    /// Gets a value indicating whether the waveform data is 16-bit.
    /// </summary>
    public bool Is16Bit { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether the DSP fell back to 8-bit DMA for a 16-bit transfer.
    /// </summary>
    public bool IsUsing16BitAliasedDma { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the waveform data is stereo.
    /// </summary>
    public bool IsStereo { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether a DMA transfer is active.
    /// </summary>
    public bool IsDmaTransferActive { get; set; }

    /// <summary>
    ///     Currently active DMA channel.
    /// </summary>
    internal DmaChannel? CurrentChannel { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether the internal waveform buffer reached capacity.
    /// </summary>
    public bool IsAtCapacity => _waveBuffer.IsAtCapacity;

    /// <summary>
    /// Starts a new DMA transfer.
    /// </summary>
    /// <param name="is16Bit">Value indicating whether this is a 16-bit transfer.</param>
    /// <param name="isStereo">Value indicating whether this is a stereo transfer.</param>
    /// <param name="compressionLevel">Compression level of the expected data.</param>
    /// <param name="referenceByte">Value indicating whether a reference byte is expected.</param>
    public void Begin(bool is16Bit, bool isStereo, CompressionLevel compressionLevel = CompressionLevel.None,
        bool referenceByte = false) {
        Is16Bit = is16Bit;
        IsStereo = isStereo;
        IsUsing16BitAliasedDma = false;
        _referenceByteExpected = referenceByte;
        _compression = compressionLevel;
        IsDmaTransferActive = true;

        _decodeRemainderOffset = -1;

        _decoder = compressionLevel switch {
            CompressionLevel.ADPCM2 => _adpcm2,
            CompressionLevel.ADPCM3 => _adpcm3,
            CompressionLevel.ADPCM4 => _adpcm4,
            _ => null,
        };

        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug(
                "DSP beginning DMA transfer. 16-bit: {Is16Bit}, Stereo: {IsStereo}, Compression: {Compression}, ReferenceByte: {ReferenceByteExpected}",
                is16Bit,
                isStereo,
                compressionLevel,
                referenceByte);
        }

        if (Is16Bit) {
            if (_highDmaChannelNumber.HasValue) {
                CurrentChannel = _dmaSystem.GetChannel((byte)_highDmaChannelNumber.Value);
            }

            if (CurrentChannel is null) {
                IsUsing16BitAliasedDma = true;
                CurrentChannel = _dmaSystem.GetChannel((byte)_lowDmaChannelNumber)
                                 ?? throw new InvalidOperationException(
                                     $"DMA channel {_lowDmaChannelNumber} unavailable for aliased 16-bit transfer.");
            }
        } else {
            CurrentChannel = _dmaSystem.GetChannel((byte)_lowDmaChannelNumber)
                             ?? throw new InvalidOperationException($"DMA channel {_lowDmaChannelNumber} unavailable.");
        }

        if (_logger.IsEnabled(LogEventLevel.Debug) && CurrentChannel is not null) {
            _logger.Debug(
                "DSP bound to DMA channel {ChannelNumber} ({ChannelWidth}-bit){AliasInfo}",
                CurrentChannel.ChannelNumber,
                CurrentChannel.Is16Bit ? 16 : 8,
                IsUsing16BitAliasedDma ? " using aliased 16-bit mode" : string.Empty);
        }

        _resetTimer.Elapsed -= OnResetTimerElapsed;
        _resetTimer.Elapsed += OnResetTimerElapsed;
    }

    /// <summary>
    ///     Handles the reset timer's elapsed event by restoring the DSP to the normal state.
    /// </summary>
    private void OnResetTimerElapsed(object? sender, EventArgs e) {
        State = DspState.Normal;
        _resetTimer.Stop();
    }

    /// <summary>
    ///     Attempts to advance the current DMA transfer and fill the internal waveform buffer.
    /// </summary>
    /// <param name="requestedBytes">Optional number of bytes requested from the DMA channel.</param>
    public int PumpDma(int? requestedBytes = null) {
        if (CurrentChannel is null || !IsDmaTransferActive) {
            return 0;
        }

        return PumpDmaFromChannel(CurrentChannel, requestedBytes);
    }

    /// <summary>
    ///     Pulls data from the specified DMA channel and writes it into the waveform buffer.
    /// </summary>
    /// <param name="channel">The DMA channel to read data from.</param>
    /// <param name="requestedBytes">Optional number of bytes requested from the channel.</param>
    /// <returns>The number of bytes transferred into the waveform buffer.</returns>
    private int PumpDmaFromChannel(DmaChannel channel, int? requestedBytes) {
        int wordCountAvailable = channel.CurrentCount + 1;
        if (wordCountAvailable <= 0) {
            return 0;
        }

        int bytesAvailable = wordCountAvailable << channel.ShiftCount;
        int bytesRequested = requestedBytes ?? bytesAvailable;
        if (bytesRequested <= 0) {
            bytesRequested = bytesAvailable;
        }

        if (channel.Is16Bit) {
            bytesRequested &= ~1;
            if (bytesRequested == 0) {
                return 0;
            }
        }

        int bytesToTransfer = Math.Min(bytesRequested, bytesAvailable);
        if (bytesToTransfer <= 0) {
            return 0;
        }

        int wordsToTransfer = channel.Is16Bit ? bytesToTransfer >> 1 : bytesToTransfer;

        byte[] rented = ArrayPool<byte>.Shared.Rent(bytesToTransfer);
        try {
            Span<byte> span = rented.AsSpan(0, bytesToTransfer);
            int wordsTransferred = channel.Read(wordsToTransfer, span);
            int bytesTransferred = wordsTransferred << channel.ShiftCount;
            if (bytesTransferred <= 0) {
                _logger.Debug("DSP received no data from DMA channel {ChannelNumber} during pump operation.",
                    channel.ChannelNumber);
                return 0;
            }

            var segment = new ArraySegment<byte>(rented, 0, bytesTransferred);
            int bytesWritten = DmaWrite(segment);
            return Math.Min(bytesTransferred, bytesWritten);
        } finally {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Reads samples from the internal buffer.
    /// </summary>
    /// <param name="buffer">Buffer into which sample data is written.</param>
    public void Read(Span<byte> buffer) {
        if (_compression == CompressionLevel.None) {
            InternalRead(buffer);
            return;
        }

        if (_decodeBuffer == null || _decodeBuffer.Length < buffer.Length * 4) {
            _decodeBuffer = new byte[buffer.Length * 4];
        }

        int offset = 0;
        int length = buffer.Length;

        while (buffer.Length > 0 && _decodeRemainderOffset >= 0) {
            buffer[offset] = _decodeRemainder[_decodeRemainderOffset];
            offset++;
            length--;
            _decodeRemainderOffset--;
        }

        if (length <= 0) {
            return;
        }

        if (_referenceByteExpected) {
            InternalRead(buffer.Slice(offset, 1));
            _referenceByteExpected = false;
            if (_decoder is not null) {
                _decoder.Reference = _decodeBuffer[offset];
            }
            offset++;
            length--;
        }

        if (length <= 0) {
            return;
        }

        int? blocks = length / _decoder?.CompressionFactor;

        if (blocks > 0 && _decodeBuffer is not null) {
            InternalRead(_decodeBuffer.AsSpan(0, blocks.Value));
            _decoder?.Decode(_decodeBuffer, 0, blocks.Value, buffer[offset..]);
        }

        int? remainder = length % _decoder?.CompressionFactor;
        if (remainder > 0) {
            InternalRead(_decodeRemainder.AsSpan(0, remainder.Value));
            Array.Reverse(_decodeRemainder, 0, remainder.Value);
            _decodeRemainderOffset = remainder.Value - 1;
        }
    }

    /// <summary>
    /// Writes data from a DMA transfer.
    /// </summary>
    /// <param name="source">Pointer to data in memory.</param>
    /// <returns>Number of bytes actually written.</returns>
    public int DmaWrite(IList<byte> source) {
        int bytesWritten = _waveBuffer.Write(source);
        if (bytesWritten < source.Count) {
            _logger.Warning(
                "DSP waveform buffer accepted {BytesWritten} of {SourceCount} requested bytes; buffer is saturated.",
                bytesWritten, source.Count);
        }

        return bytesWritten;
    }

    /// <summary>
    /// Resets the DSP to its initial state.
    /// </summary>
    public void Reset() {
        State = DspState.ResetWait;
        SampleRate = 22050;
        BlockTransferSize = 65536;
        Is16Bit = false;
        IsStereo = false;
        IsUsing16BitAliasedDma = false;
        _readIdleCycles = 0;
        CurrentChannel = null;
        IsDmaTransferActive = false;
        _hasLoggedStreamStarvation = false;
        State = DspState.Reset;
        _resetTimer.Start();
        _logger.Debug("DSP reset to default state.");
    }

    private readonly Timer _resetTimer = new(TimeSpan.FromMicroseconds(20));

    /// <summary>
    /// Reads samples from the internal waveform buffer into the supplied span, blocking until data becomes available or zero-filling when idle.
    /// </summary>
    /// <param name="buffer">Buffer into which sample data is written.</param>
    private void InternalRead(Span<byte> buffer) {
        Span<byte> dest = buffer;
        SpinWait spinner = new();

        while (dest.Length > 0) {
            int amt = _waveBuffer.Read(dest);

            if (amt == 0) {
                if (IsDmaTransferActive && CurrentChannel is not null) {
                    PumpDma(dest.Length);
                    amt = _waveBuffer.Read(dest);
                    if (amt > 0) {
                        _readIdleCycles = 0;
                        _hasLoggedStreamStarvation = false;
                        spinner.Reset();
                        dest = dest[amt..];
                        continue;
                    }
                }

                if (!IsDmaTransferActive) {
                    byte zeroValue = Is16Bit ? (byte)0 : (byte)128;
                    dest.Fill(zeroValue);
                    _readIdleCycles = 0;
                    _hasLoggedStreamStarvation = false;
                    return;
                }

                _readIdleCycles++;

                switch (_readIdleCycles) {
                    case < 250: {
                        spinner.SpinOnce();
                        if (spinner.NextSpinWillYield) {
                            Thread.Yield();
                        }

                        continue;
                    }
                    case < 4000:
                        spinner.Reset();
                        HighResolutionWaiter.WaitMilliseconds(0.25);
                        continue;
                }

                int idleCycles = _readIdleCycles;
                byte zeroValueFallback = Is16Bit ? (byte)0 : (byte)128;
                dest.Fill(zeroValueFallback);
                if (!_hasLoggedStreamStarvation) {
                    _logger.Warning(
                        "DSP filled output with silence after {IdleCycles} idle cycles while DMA was active.",
                        idleCycles);
                }
                _readIdleCycles = 0;
                _hasLoggedStreamStarvation = true;
                return;
            }

            _readIdleCycles = 0;
            _hasLoggedStreamStarvation = false;
            spinner.Reset();
            dest = dest[amt..];
        }
    }

    /// <summary>
    /// Number of cycles with no new input data.
    /// </summary>
    private int _readIdleCycles;

    /// <summary>
    /// The current compression level.
    /// </summary>
    private CompressionLevel _compression;

    /// <summary>
    /// Indicates whether a reference byte is expected.
    /// </summary>
    private bool _referenceByteExpected;

    /// <summary>
    /// Current ADPCM decoder instance.
    /// </summary>
    private ADPCMDecoder? _decoder;

    /// <summary>
    /// Buffer used for ADPCM decoding.
    /// </summary>
    private byte[]? _decodeBuffer;

    /// <summary>
    /// Last index of remaining decoded bytes.
    /// </summary>
    private int _decodeRemainderOffset;
    private bool _disposedValue;
    private bool _hasLoggedStreamStarvation;

    /// <summary>
    /// Remaining decoded bytes.
    /// </summary>
    private readonly byte[] _decodeRemainder = new byte[4];

    /// <summary>
    /// Contains generated waveform data waiting to be read.
    /// </summary>
    private readonly CircularBuffer _waveBuffer = new(TargetBufferSize);

    /// <summary>
    /// Size of output buffer in samples.
    /// </summary>
    private const int TargetBufferSize = 32768;

    private void Dispose(bool disposing) {
        if (_disposedValue) {
            return;
        }

        if (disposing) {
            _resetTimer.Dispose();
        }

        _disposedValue = true;
    }

    /// <inheritdoc/>
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
    }
}
