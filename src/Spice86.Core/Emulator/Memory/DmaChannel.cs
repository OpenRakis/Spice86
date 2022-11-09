namespace Spice86.Core.Emulator.Memory;

using Spice86.Core.Emulator.Devices.Timer;

using System;
using System.Diagnostics;

/// <summary>
/// Contains information about a DMA channel.
/// </summary>
public sealed class DmaChannel {
    private bool _isActive;
    private bool _addressByteRead;
    private bool _addressByteWritten;
    private bool _countByteRead;
    private bool _countByteWritten;
    private volatile int _bytesRemaining;
    private byte _bytesRemainingHighByte;
    private byte _addressHighByte;
    private int _transferRate;
    private readonly Stopwatch _transferTimer = new();

    internal DmaChannel() {
    }

    /// <summary>
    /// Gets or sets a value indicating whether a DMA transfer is active.
    /// </summary>
    public bool IsActive {
        get => _isActive;
        set {
            if (_isActive != value) {
                if (value) {
                    _transferTimer.Start();
                } else {
                    _transferTimer.Reset();
                }
                _isActive = value;
            }
        }
    }
    /// <summary>
    /// Gets a value indicating whether the channel is masked (disabled).
    /// </summary>
    public bool IsMasked { get; internal set; }
    /// <summary>
    /// Gets the current DMA transfer mode of the channel.
    /// </summary>
    public DmaTransferMode TransferMode { get; internal set; }
    /// <summary>
    /// Gets the DMA transfer memory page.
    /// </summary>
    public byte Page {
        get;
        internal set;
    }
    /// <summary>
    /// Gets the DMA transfer memory address.
    /// </summary>
    public ushort Address { get; internal set; }
    /// <summary>
    /// Gets the number of bytes to transfer.
    /// </summary>
    public ushort Count { get; internal set; }
    public int TransferBytesRemaining {
        get => _bytesRemaining;
        internal set => _bytesRemaining = value;
    }
    /// <summary>
    /// Gets or sets the desired transfer rate in bytes/second.
    /// </summary>
    public int TransferRate {
        get => _transferRate;
        set {
            int period = 1;
            int chunkSize = value / 1000;

            if (chunkSize < 1) {
                chunkSize = 1;
                period = value / 1000;
            }

            _transferRate = value;
            TransferPeriod = Timer.StopwatchTicksPerMillisecond * period;
            TransferChunkSize = chunkSize;
        }
    }

    /// <summary>
    /// Gets or sets the device which is connected to the DMA channel.
    /// </summary>
    internal IDmaDevice8? Device { get; set; }

    /// <summary>
    /// Gets or sets the period between DMA transfers in stopwatch ticks.
    /// </summary>
    private long TransferPeriod { get; set; }
    /// <summary>
    /// Gets or sets the size of each DMA transfer chunk.
    /// </summary>
    private int TransferChunkSize { get; set; }

    /// <summary>
    /// Returns a string representation of the DmaChannel.
    /// </summary>
    /// <returns>String representation of the DmaChannel.</returns>
    public override string ToString() => $"{Page:X2}:{Address:X4}";

    /// <summary>
    /// Returns the next byte of the memory address.
    /// </summary>
    /// <returns>Next byte of the memory address.</returns>
    internal byte ReadAddressByte() {
        try {
            if (!_addressByteRead) {
                ushort address = (ushort)(Address + Count - (TransferBytesRemaining - 1));
                _addressHighByte = (byte)(address >> 8);
                return (byte)(address & 0xFF);
            } else {
                return _addressHighByte;
            }
        } finally {
            _addressByteRead = !_addressByteRead;
        }
    }
    /// <summary>
    /// Writes the next byte of the memory address.
    /// </summary>
    /// <param name="value">Next byte of the memory address.</param>
    internal void WriteAddressByte(byte value) {
        try {
            if (!_addressByteWritten) {
                Address = value;
            } else {
                Address |= (ushort)(value << 8);
            }
        } finally {
            _addressByteWritten = !_addressByteWritten;
        }
    }
    /// <summary>
    /// Returns the next byte of the memory address.
    /// </summary>
    /// <returns>Next byte of the memory address.</returns>
    internal byte ReadCountByte() {
        try {
            if (!_countByteRead) {
                ushort count = (ushort)(TransferBytesRemaining - 1);
                _bytesRemainingHighByte = (byte)(count >> 8 & 0xFF);
                return (byte)(count & 0xFF);
            } else {
                return _bytesRemainingHighByte;
            }
        } finally {
            _countByteRead = !_countByteRead;
        }
    }
    /// <summary>
    /// Writes the next byte of the memory address.
    /// </summary>
    /// <param name="value">Next byte of the memory address.</param>
    internal void WriteCountByte(byte value) {
        try {
            if (!_countByteWritten) {
                Count = value;
            } else {
                Count |= (ushort)(value << 8);
                TransferBytesRemaining = Count + 1;
            }
        } finally {
            _countByteWritten = !_countByteWritten;
        }
    }

    private bool MustTransferData =>
        Device is not null && !IsMasked && IsActive && _transferTimer.ElapsedTicks >= TransferPeriod;

    /// <summary>
    /// Performs a DMA transfer.
    /// </summary>
    /// <param name="memory">Current PhysicalMemory instance.</param>
    /// <remarks>
    /// This method should only be called if the channel is active.
    /// </remarks>
    internal bool Transfer(Memory memory) {
        if (!MustTransferData) {
            return false;
        }
        IDmaDevice8? device = Device;
        if (device is null) {
            return false;
        }
        uint memoryAddress = (uint)Page << 16 | Address;
        uint sourceOffset = (uint)Count + 1 - (uint)TransferBytesRemaining;

        int count = Math.Min(TransferChunkSize, TransferBytesRemaining);
        int startAddress = (int)(memoryAddress + sourceOffset);
        Span<byte> source = memory.GetSpan(startAddress, count);

        count = device.WriteBytes(source);
        TransferBytesRemaining -= count;

        if (TransferBytesRemaining <= 0) {
            if (TransferMode == DmaTransferMode.SingleCycle) {
                IsActive = false;
                device.SingleCycleComplete();
            } else {
                TransferBytesRemaining = Count + 1;
            }
        }
        _transferTimer.Restart();
        return true;
    }
}