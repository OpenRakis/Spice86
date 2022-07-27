namespace Spice86.Emulator.VM;

using Spice86.Emulator.Devices.Timer;
using Spice86.Emulator.Memory;

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
        get => this._isActive;
        set {
            if (this._isActive != value) {
                if (value) {
                    this._transferTimer.Start();
                } else {
                    this._transferTimer.Reset();
                }
                this._isActive = value;
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
        get => this._transferRate;
        set {
            int period = 1;
            int chunkSize = value / 1000;

            if (chunkSize < 1) {
                chunkSize = 1;
                period = value / 1000;
            }

            this._transferRate = value;
            this.TransferPeriod = Timer.StopwatchTicksPerMillisecond * period;
            this.TransferChunkSize = chunkSize;
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
    public override string ToString() => $"{this.Page:X2}:{this.Address:X4}";

    /// <summary>
    /// Returns the next byte of the memory address.
    /// </summary>
    /// <returns>Next byte of the memory address.</returns>
    internal byte ReadAddressByte() {
        try {
            if (!this._addressByteRead) {
                ushort address = (ushort)(this.Address + this.Count - (this.TransferBytesRemaining - 1));
                this._addressHighByte = (byte)(address >> 8);
                return (byte)(address & 0xFF);
            } else {
                return this._addressHighByte;
            }
        } finally {
            this._addressByteRead = !this._addressByteRead;
        }
    }
    /// <summary>
    /// Writes the next byte of the memory address.
    /// </summary>
    /// <param name="value">Next byte of the memory address.</param>
    internal void WriteAddressByte(byte value) {
        try {
            if (!this._addressByteWritten) {
                this.Address = value;
            } else {
                this.Address |= (ushort)(value << 8);
            }
        } finally {
            this._addressByteWritten = !this._addressByteWritten;
        }
    }
    /// <summary>
    /// Returns the next byte of the memory address.
    /// </summary>
    /// <returns>Next byte of the memory address.</returns>
    internal byte ReadCountByte() {
        try {
            if (!this._countByteRead) {
                ushort count = (ushort)(this.TransferBytesRemaining - 1);
                this._bytesRemainingHighByte = (byte)((count >> 8) & 0xFF);
                return (byte)(count & 0xFF);
            } else {
                return _bytesRemainingHighByte;
            }
        } finally {
            this._countByteRead = !this._countByteRead;
        }
    }
    /// <summary>
    /// Writes the next byte of the memory address.
    /// </summary>
    /// <param name="value">Next byte of the memory address.</param>
    internal void WriteCountByte(byte value) {
        try {
            if (!this._countByteWritten) {
                this.Count = value;
            } else {
                this.Count |= (ushort)(value << 8);
                this.TransferBytesRemaining = this.Count + 1;
            }
        } finally {
            this._countByteWritten = !this._countByteWritten;
        }
    }

    public bool MustTransferData =>
        Device is not null && this._transferTimer.ElapsedTicks >= this.TransferPeriod;

    /// <summary>
    /// Performs a DMA transfer.
    /// </summary>
    /// <param name="memory">Current PhysicalMemory instance.</param>
    /// <remarks>
    /// This method should only be called if the channel is active.
    /// </remarks>
    internal void Transfer(Memory memory) {
        IDmaDevice8? device = this.Device;
        if (MustTransferData && device is not null) {
            uint memoryAddress = ((uint)this.Page << 16) | this.Address;
            uint sourceOffset = (uint)this.Count + 1 - (uint)this.TransferBytesRemaining;

            int count = Math.Min(this.TransferChunkSize, this.TransferBytesRemaining);
            int startAddress = (int)(memoryAddress + sourceOffset);
            Span<byte> source = memory.GetSpan(startAddress, count);

            count = device.WriteBytes(source);
            this.TransferBytesRemaining -= count;

            if (this.TransferBytesRemaining <= 0) {
                if (this.TransferMode == DmaTransferMode.SingleCycle) {
                    this.IsActive = false;
                    device.SingleCycleComplete();
                } else {
                    this.TransferBytesRemaining = this.Count + 1;
                }
            }
            this._transferTimer.Restart();
        }
    }
}