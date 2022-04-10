namespace Spice86.Emulator.Sound.Blaster;

using System;
using System.Threading;

using Spice86.Emulator.VM;

/// <summary>
/// Emulates the Sound Blaster 16 DSP.
/// </summary>
internal sealed class Dsp {
    /// <summary>
    /// Initializes a new instance of the Dsp class.
    /// </summary>
    /// <param name="vm">Virtual machine instance associated with the DSP.</param>
    /// <param name="dma8">8-bit DMA channel for the DSP device.</param>
    /// <param name="dma16">16-bit DMA channel for the DSP device.</param>
    public Dsp(Machine vm, int dma8, int dma16) {
        this.dmaChannel8 = vm.DmaController.Channels[dma8];
        this.dmaChannel16 = vm.DmaController.Channels[dma16];
        this.SampleRate = 22050;
        this.BlockTransferSize = 65536;
    }

    /// <summary>
    /// Occurs when a buffer has been transferred in auto-initialize mode.
    /// </summary>
    public event EventHandler? AutoInitBufferComplete;

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
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Starts a new DMA transfer.
    /// </summary>
    /// <param name="is16Bit">Value indicating whether this is a 16-bit transfer.</param>
    /// <param name="isStereo">Value indicating whether this is a stereo transfer.</param>
    /// <param name="autoInitialize">Value indicating whether the DMA controller is in auto-initialize mode.</param>
    /// <param name="compression">Compression level of the expected data.</param>
    /// <param name="referenceByte">Value indicating whether a reference byte is expected.</param>
    public void Begin(bool is16Bit, bool isStereo, bool autoInitialize, CompressionLevel compression = CompressionLevel.None, bool referenceByte = false) {
        this.Is16Bit = is16Bit;
        this.IsStereo = isStereo;
        this.AutoInitialize = autoInitialize;
        this.referenceByteExpected = referenceByte;
        this.compression = compression;
        this.IsEnabled = true;

        this.decodeRemainderOffset = -1;

        this.decoder = compression switch {
            CompressionLevel.ADPCM2 => new ADPCM2(),
            CompressionLevel.ADPCM3 => new ADPCM3(),
            CompressionLevel.ADPCM4 => new ADPCM4(),
            _ => null,
        };

        this.currentChannel = this.dmaChannel8;

        int transferRate = this.SampleRate;
        if (this.Is16Bit) {
            transferRate *= 2;
        }

        if (this.IsStereo) {
            transferRate *= 2;
        }

        double factor = 1.0;
        if (autoInitialize) {
            factor = 1.5;
        }

        this.currentChannel.TransferRate = (int)(transferRate * factor);
        this.currentChannel.IsActive = true;
    }
    /// <summary>
    /// Exits autoinitialize mode.
    /// </summary>
    public void ExitAutoInit() {
        this.AutoInitialize = false;
    }
    /// <summary>
    /// Reads samples from the internal buffer.
    /// </summary>
    /// <param name="buffer">Buffer into which sample data is written.</param>
    public void Read(Span<byte> buffer) {
        if (this.compression == CompressionLevel.None) {
            this.InternalRead(buffer);
            return;
        }

        if (this.decodeBuffer == null || this.decodeBuffer.Length < buffer.Length * 4) {
            this.decodeBuffer = new byte[buffer.Length * 4];
        }

        int offset = 0;
        int length = buffer.Length;

        while (buffer.Length > 0 && this.decodeRemainderOffset >= 0) {
            buffer[offset] = this.decodeRemainder[this.decodeRemainderOffset];
            offset++;
            length--;
            this.decodeRemainderOffset--;
        }

        if (length <= 0) {
            return;
        }

        if (this.referenceByteExpected) {
            this.InternalRead(buffer.Slice(offset, 1));
            this.referenceByteExpected = false;
            if (this.decoder is not null) {
                this.decoder.Reference = decodeBuffer[offset];
            }
            offset++;
            length--;
        }

        if (length <= 0) {
            return;
        }

        int? blocks = length / this.decoder?.CompressionFactor;

        if (blocks > 0 && this.decodeBuffer is not null) {
            this.InternalRead(this.decodeBuffer.AsSpan(0, blocks.Value));
            if (this.decoder is not null) {
                this.decoder.Decode(this.decodeBuffer, 0, blocks.Value, buffer[offset..]);
            }
        }

        int? remainder = length % this.decoder?.CompressionFactor;
        if (remainder > 0) {
            this.InternalRead(this.decodeRemainder.AsSpan(0, remainder.Value));
            Array.Reverse(this.decodeRemainder, 0, remainder.Value);
            this.decodeRemainderOffset = remainder.Value - 1;
        }
    }
    /// <summary>
    /// Writes data from a DMA transfer.
    /// </summary>
    /// <param name="source">Pointer to data in memory.</param>
    /// <returns>Number of bytes actually written.</returns>
    public int DmaWrite(ReadOnlySpan<byte> source) {
        int actualCount = this.waveBuffer.Write(source);

        if (this.AutoInitialize) {
            this.autoInitTotal += actualCount;
            if (this.autoInitTotal >= this.BlockTransferSize) {
                this.autoInitTotal -= this.BlockTransferSize;
                OnAutoInitBufferComplete(EventArgs.Empty);
            }
        }

        return actualCount;
    }
    /// <summary>
    /// Resets the DSP to its initial state.
    /// </summary>
    public void Reset() {
        this.SampleRate = 22050;
        this.BlockTransferSize = 65536;
        this.AutoInitialize = false;
        this.Is16Bit = false;
        this.IsStereo = false;
        this.autoInitTotal = 0;
        this.readIdleCycles = 0;
    }

    /// <summary>
    /// Reads samples from the internal buffer.
    /// </summary>
    /// <param name="buffer">Buffer into which sample data is written.</param>
    private void InternalRead(Span<byte> buffer) {
        Span<byte> dest = buffer;

        while (dest.Length > 0) {
            int amt = waveBuffer.Read(dest);

            if (amt == 0) {
                if (!this.IsEnabled || this.readIdleCycles >= 100) {
                    byte zeroValue = this.Is16Bit ? (byte)0 : (byte)128;
                    dest.Fill(zeroValue);
                    return;
                }

                this.readIdleCycles++;
                Thread.Sleep(1);
            } else {
                this.readIdleCycles = 0;
            }

            dest = dest[amt..];
        }
    }
    /// <summary>
    /// Raises the AutoInitBufferComplete event.
    /// </summary>
    /// <param name="e">Unused EventArgs instance.</param>
    private void OnAutoInitBufferComplete(EventArgs e) => this.AutoInitBufferComplete?.Invoke(this, e);

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
    private const int TargetBufferSize = 2048;
}
