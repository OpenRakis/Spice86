﻿namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

using Spice86.Core.Emulator.Memory;

using System;
using System.Threading;

/// <summary>
/// Emulates the Sound Blaster 16 DSP.
/// </summary>
public sealed class Dsp : IDisposable {
    private readonly ADPCM2 _adpcm2;
    private readonly ADPCM3 _adpcm3;
    private readonly ADPCM4 _adpcm4;

    /// <summary>
    /// Initializes a new instance of the Digital Signal Processor.
    /// </summary>
    /// <param name="eightBitDmaChannel">The 8-bit wide DMA channel</param>
    /// <param name="sixteenBitDmaChannel">The 16-bit wide DMA channel</param>
    public Dsp(DmaChannel eightBitDmaChannel, DmaChannel sixteenBitDmaChannel) {
        _adpcm2 = new();
        _adpcm3 = new();
        _adpcm4 = new();
        _dmaChannel8 = eightBitDmaChannel;
        _dmaChannel16 = sixteenBitDmaChannel;
        SampleRate = 22050;
        BlockTransferSize = 65536;
    }

    /// <summary>
    /// Gets whether the DSP can receive PCM data
    /// </summary>
    /// <returns></returns>
    public bool IsWriteBufferAtCapacity() {
        if (State == DspState.Normal) {
            return true;
        }
        if (IsDmaTransferActive) {
            return true;
        }
        return IsAtCapacity;
    }

    /// <summary>
    /// Gets or sets the current DSP State
    /// </summary>
    public DspState State { get; private set; }

    /// <summary>
    /// Occurs when a buffer has been transferred in auto-initialize mode.
    /// </summary>
    public event Action? OnAutoInitBufferComplete;

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

    public bool IsAtCapacity => _waveBuffer.IsAtCapacity;

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

        _currentChannel = _dmaChannel8;

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

        _currentChannel.TransferRate = (int)(transferRate * factor);
        _currentChannel.IsActive = true;

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
    public int DmaWrite(ReadOnlySpan<byte> source) {
        int actualCount = _waveBuffer.Write(source);
        if (AutoInitialize) {
            _autoInitTotal += actualCount;
            if (_autoInitTotal >= BlockTransferSize) {
                _autoInitTotal -= BlockTransferSize;
                OnAutoInitBufferComplete?.Invoke();
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
        _autoInitTotal = 0;
        _readIdleCycles = 0;
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
            int amt = _waveBuffer.Read(dest);

            if (amt == 0) {
                if (!IsDmaTransferActive || _readIdleCycles >= 100) {
                    byte zeroValue = Is16Bit ? (byte)0 : (byte)128;
                    dest.Fill(zeroValue);
                    return;
                }

                _readIdleCycles++;
                Thread.Sleep(1);
            } else {
                _readIdleCycles = 0;
            }

            dest = dest[amt..];
        }
    }

    /// <summary>
    /// DMA channel used for 8-bit data transfers.
    /// </summary>
    private readonly DmaChannel _dmaChannel8;

    /// <summary>
    /// DMA channel used for 16-bit data transfers.
    /// </summary>
    private readonly DmaChannel _dmaChannel16;

    /// <summary>
    /// Currently active DMA channel.
    /// </summary>
    private DmaChannel? _currentChannel;

    /// <summary>
    /// Number of bytes transferred in the current auto-init cycle.
    /// </summary>
    private int _autoInitTotal;

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
    private const int TargetBufferSize = 1024;

    private void Dispose(bool disposing) {
        if (!_disposedValue) {
            if (disposing) {
                _resetTimer.Dispose();
            }
            _disposedValue = true;
        }
    }

    /// <inheritdoc/>
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
