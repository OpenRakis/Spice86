namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Memory;

using System;
using System.Threading;

/// <summary>
/// Emulates the Sound Blaster 16 DSP.
/// </summary>
public sealed class Dsp : IDisposable {
    private readonly IRequestInterrupt _soundCard;

    /// <summary>
    /// Initializes a new instance of the Digital Signal Processor.
    /// </summary>
    /// <param name="eightBitDmaChannel">The 8-bit wide DMA channel</param>
    /// <param name="sixteenBitDmaChannel">The 16-bit wide DMA channel</param>
    /// <param name="soundCard">The host sound-card, used to raise interrupts.</param>
    public Dsp(DmaChannel eightBitDmaChannel, DmaChannel sixteenBitDmaChannel, IRequestInterrupt soundCard) {
        dmaChannel8 = eightBitDmaChannel;
        dmaChannel16 = sixteenBitDmaChannel;
        SampleRate = 22050;
        BlockTransferSize = 65536;
        _soundCard = soundCard;
    }

    /// <summary>
    /// Gets or sets the current DSP State
    /// </summary>
    public DspState State { get; private set; }

    /// <summary>
    /// Occurs when a buffer has been transferred in auto-initialize mode.
    /// </summary>
    private void OnAutoInitBufferComplete() => _soundCard.RaiseInterruptRequest();

    /// <summary>
    /// Gets or sets the DSP's sample rate.
    /// </summary>
    public int SampleRate { get; set; }

    /// <summary>
    /// Gets a value indicating whether the DMA mode is set to auto-initialize.
    /// </summary>
    public bool AutoInitialize { get; private set; }

    /// <summary>
    /// Gets or sets the size of a transfer block for auto-init mode.
    /// </summary>
    public int BlockTransferSize { get; set; }

    /// <summary>
    /// Gets a value indicating whether the waveform data is 16-bit.
    /// </summary>
    public bool Is16Bit { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the waveform data is stereo.
    /// </summary>
    public bool IsStereo { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether a DMA transfer is active.
    /// </summary>
    public bool IsDmaTransferActive { get; set; }

    /// <summary>
    /// Starts a new DMA transfer.
    /// </summary>
    /// <param name="is16Bit">Value indicating whether this is a 16-bit transfer.</param>
    /// <param name="isStereo">Value indicating whether this is a stereo transfer.</param>
    /// <param name="autoInitialize">Value indicating whether the DMA controller is in auto-initialize mode.</param>
    /// <param name="compressionLevel">Compression level of the expected data.</param>
    /// <param name="referenceByte">Value indicating whether a reference byte is expected.</param>
    public void Begin(bool is16Bit, bool isStereo, bool autoInitialize, CompressionLevel compressionLevel = CompressionLevel.None, bool referenceByte = false) {
        Is16Bit = is16Bit;
        IsStereo = isStereo;
        AutoInitialize = autoInitialize;
        referenceByteExpected = referenceByte;
        compression = compressionLevel;
        IsDmaTransferActive = true;

        decodeRemainderOffset = -1;

        decoder = compressionLevel switch {
            CompressionLevel.ADPCM2 => new ADPCM2(),
            CompressionLevel.ADPCM3 => new ADPCM3(),
            CompressionLevel.ADPCM4 => new ADPCM4(),
            _ => null,
        };

        currentChannel = dmaChannel8;

        int transferRate = SampleRate;
        if (Is16Bit) {
            transferRate *= 2;
        }

        if (IsStereo) {
            transferRate *= 2;
        }

        double factor = 1.0;
        if (autoInitialize) {
            factor = 1.5;
        }

        currentChannel.TransferRate = (int)(transferRate * factor);
        currentChannel.IsActive = true;

        _resetTimer.Elapsed += OnResetTimerElapsed;
    }

    private void OnResetTimerElapsed(object? sender, EventArgs e) {
        State = DspState.Normal;
        _resetTimer.Stop();
    }

    /// <summary>
    /// Exits autoinitialize mode.
    /// </summary>
    public void ExitAutoInit() {
        AutoInitialize = false;
    }

    /// <summary>
    /// Reads samples from the internal buffer.
    /// </summary>
    /// <param name="buffer">Buffer into which sample data is written.</param>
    public void Read(Span<byte> buffer) {
        if (compression == CompressionLevel.None) {
            InternalRead(buffer);
            return;
        }

        if (decodeBuffer == null || decodeBuffer.Length < buffer.Length * 4) {
            decodeBuffer = new byte[buffer.Length * 4];
        }

        int offset = 0;
        int length = buffer.Length;

        while (buffer.Length > 0 && decodeRemainderOffset >= 0) {
            buffer[offset] = decodeRemainder[decodeRemainderOffset];
            offset++;
            length--;
            decodeRemainderOffset--;
        }

        if (length <= 0) {
            return;
        }

        if (referenceByteExpected) {
            InternalRead(buffer.Slice(offset, 1));
            referenceByteExpected = false;
            if (decoder is not null) {
                decoder.Reference = decodeBuffer[offset];
            }
            offset++;
            length--;
        }

        if (length <= 0) {
            return;
        }

        int? blocks = length / decoder?.CompressionFactor;

        if (blocks > 0 && decodeBuffer is not null) {
            InternalRead(decodeBuffer.AsSpan(0, blocks.Value));
            decoder?.Decode(decodeBuffer, 0, blocks.Value, buffer[offset..]);
        }

        int? remainder = length % decoder?.CompressionFactor;
        if (remainder > 0) {
            InternalRead(decodeRemainder.AsSpan(0, remainder.Value));
            Array.Reverse(decodeRemainder, 0, remainder.Value);
            decodeRemainderOffset = remainder.Value - 1;
        }
    }

    /// <summary>
    /// Writes data from a DMA transfer.
    /// </summary>
    /// <param name="source">Pointer to data in memory.</param>
    /// <returns>Number of bytes actually written.</returns>
    public int DmaWrite(ReadOnlySpan<byte> source) {
        int actualCount = waveBuffer.Write(source);
        if (AutoInitialize) {
            autoInitTotal += actualCount;
            if (autoInitTotal >= BlockTransferSize) {
                autoInitTotal -= BlockTransferSize;
                OnAutoInitBufferComplete();
            }
        }

        return actualCount;
    }

    /// <summary>
    /// Resets the DSP to its initial state.
    /// </summary>
    public void Reset() {
        State = DspState.ResetWait;
        SampleRate = 22050;
        BlockTransferSize = 65536;
        AutoInitialize = false;
        Is16Bit = false;
        IsStereo = false;
        autoInitTotal = 0;
        readIdleCycles = 0;
        State = DspState.Reset;
        _resetTimer.Start();
    }

    private readonly System.Timers.Timer _resetTimer = new System.Timers.Timer(TimeSpan.FromMicroseconds(20));

    /// <summary>
    /// Reads samples from the internal buffer.
    /// </summary>
    /// <param name="buffer">Buffer into which sample data is written.</param>
    private void InternalRead(Span<byte> buffer) {
        Span<byte> dest = buffer;

        while (dest.Length > 0) {
            int amt = waveBuffer.Read(dest);

            if (amt == 0) {
                if (!IsDmaTransferActive || readIdleCycles >= 100) {
                    byte zeroValue = Is16Bit ? (byte)0 : (byte)128;
                    dest.Fill(zeroValue);
                    return;
                }

                readIdleCycles++;
                Thread.Sleep(1);
            } else {
                readIdleCycles = 0;
            }

            dest = dest[amt..];
        }
    }

    /// <summary>
    /// DMA channel used for 8-bit data transfers.
    /// </summary>
    private readonly DmaChannel dmaChannel8;

    /// <summary>
    /// DMA channel used for 16-bit data transfers.
    /// </summary>
    private readonly DmaChannel dmaChannel16;

    /// <summary>
    /// Currently active DMA channel.
    /// </summary>
    private DmaChannel? currentChannel;

    /// <summary>
    /// Number of bytes transferred in the current auto-init cycle.
    /// </summary>
    private int autoInitTotal;

    /// <summary>
    /// Number of cycles with no new input data.
    /// </summary>
    private int readIdleCycles;

    /// <summary>
    /// The current compression level.
    /// </summary>
    private CompressionLevel compression;

    /// <summary>
    /// Indicates whether a reference byte is expected.
    /// </summary>
    private bool referenceByteExpected;

    /// <summary>
    /// Current ADPCM decoder instance.
    /// </summary>
    private ADPCMDecoder? decoder;

    /// <summary>
    /// Buffer used for ADPCM decoding.
    /// </summary>
    private byte[]? decodeBuffer;

    /// <summary>
    /// Last index of remaining decoded bytes.
    /// </summary>
    private int decodeRemainderOffset;
    private bool disposedValue;

    /// <summary>
    /// Remaining decoded bytes.
    /// </summary>
    private readonly byte[] decodeRemainder = new byte[4];

    /// <summary>
    /// Contains generated waveform data waiting to be read.
    /// </summary>
    private readonly CircularBuffer waveBuffer = new(TargetBufferSize);

    /// <summary>
    /// Size of output buffer in samples.
    /// </summary>
    private const int TargetBufferSize = 1024;

    private void Dispose(bool disposing) {
        if (!disposedValue) {
            if (disposing) {
                _resetTimer.Dispose();
            }
            disposedValue = true;
        }
    }

    /// <inheritdoc/>
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
