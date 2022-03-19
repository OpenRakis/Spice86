namespace Spice86.Emulator.VM;

using System;
using System.Diagnostics;
using System.IO;

using Spice86.Emulator.InterruptHandlers;
using Spice86.Emulator.Devices.Timer;
using Spice86.Emulator.Memory;

/// <summary>
/// Contains information about a DMA channel.
/// </summary>
public sealed class DmaChannel
{
    private bool isActive;
    private bool addressByteRead;
    private bool addressByteWritten;
    private bool countByteRead;
    private bool countByteWritten;
    private volatile int bytesRemaining;
    private byte bytesRemainingHighByte;
    private byte addressHighByte;
    private int transferRate;
    private Stopwatch transferTimer = new Stopwatch();

    internal DmaChannel()
    {
    }

    /// <summary>
    /// Occurs when the <see cref="IsActive"/> property has changed.
    /// </summary>
    internal event EventHandler? IsActiveChanged;

    /// <summary>
    /// Gets or sets a value indicating whether a DMA transfer is active.
    /// </summary>
    public bool IsActive
    {
        get => this.isActive;
        set
        {
            if (this.isActive != value)
            {
                if (value) {
                    this.transferTimer.Start();
                } else {
                    this.transferTimer.Reset();
                }

                this.isActive = value;
                OnIsActiveChanged(EventArgs.Empty);
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
    public byte Page { get; internal set; }
    /// <summary>
    /// Gets the DMA transfer memory address.
    /// </summary>
    public ushort Address { get; internal set; }
    /// <summary>
    /// Gets the number of bytes to transfer.
    /// </summary>
    public ushort Count { get; internal set; }
    public int TransferBytesRemaining
    {
        get => bytesRemaining;
        internal set => bytesRemaining = value;
    }
    /// <summary>
    /// Gets or sets the desired transfer rate in bytes/second.
    /// </summary>
    public int TransferRate
    {
        get => this.transferRate;
        set
        {
            int period = 1;
            int chunkSize = value / 1000;

            if (chunkSize < 1)
            {
                chunkSize = 1;
                period = value / 1000;
            }

            this.transferRate = value;
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
    internal byte ReadAddressByte()
    {
        try
        {
            if (!this.addressByteRead)
            {
                ushort address = (ushort)(this.Address + this.Count - (this.TransferBytesRemaining - 1));
                this.addressHighByte = (byte)(address >> 8);
                return (byte)(address & 0xFF);
            }
            else
            {
                return this.addressHighByte;
            }
        }
        finally
        {
            this.addressByteRead = !this.addressByteRead;
        }
    }
    /// <summary>
    /// Writes the next byte of the memory address.
    /// </summary>
    /// <param name="value">Next byte of the memory address.</param>
    internal void WriteAddressByte(byte value)
    {
        try
        {
            if (!this.addressByteWritten) {
                this.Address = value;
            } else {
                this.Address |= (ushort)(value << 8);
            }
        }
        finally
        {
            this.addressByteWritten = !this.addressByteWritten;
        }
    }
    /// <summary>
    /// Returns the next byte of the memory address.
    /// </summary>
    /// <returns>Next byte of the memory address.</returns>
    internal byte ReadCountByte()
    {
        try
        {
            if (!this.countByteRead)
            {
                ushort count = (ushort)(this.TransferBytesRemaining - 1);
                this.bytesRemainingHighByte = (byte)((count >> 8) & 0xFF);
                return (byte)(count & 0xFF);
            }
            else
            {
                return bytesRemainingHighByte;
            }
        }
        finally
        {
            this.countByteRead = !this.countByteRead;
        }
    }
    /// <summary>
    /// Writes the next byte of the memory address.
    /// </summary>
    /// <param name="value">Next byte of the memory address.</param>
    internal void WriteCountByte(byte value)
    {
        try
        {
            if (!this.countByteWritten)
            {
                this.Count = value;
            }
            else
            {
                this.Count |= (ushort)(value << 8);
                this.TransferBytesRemaining = this.Count + 1;
            }
        }
        finally
        {
            this.countByteWritten = !this.countByteWritten;
        }
    }
    /// <summary>
    /// Performs a DMA transfer.
    /// </summary>
    /// <param name="memory">Current PhysicalMemory instance.</param>
    /// <remarks>
    /// This method should only be called if the channel is active.
    /// </remarks>
    internal void Transfer(Memory memory)
    {
        IDmaDevice8? device = this.Device;
        if (device != null && this.transferTimer.ElapsedTicks >= this.TransferPeriod)
        {
            uint memoryAddress = ((uint)this.Page << 16) | this.Address;
            uint sourceOffset = (uint)this.Count + 1 - (uint)this.TransferBytesRemaining;

            int count = Math.Min(this.TransferChunkSize, this.TransferBytesRemaining);
            byte[]? source = memory.GetData(memoryAddress + sourceOffset, count);

            count = device.WriteBytes(source);

            this.TransferBytesRemaining -= count;

            if (this.TransferBytesRemaining <= 0)
            {
                if (this.TransferMode == DmaTransferMode.SingleCycle)
                {
                    this.IsActive = false;
                    device.SingleCycleComplete();
                }
                else {
                    this.TransferBytesRemaining = this.Count + 1;
                }
            }

            this.transferTimer.Reset();
            this.transferTimer.Start();
        }
    }
    internal void Serialize(BinaryWriter writer)
    {
        writer.Write(this.isActive);
        writer.Write(this.addressByteRead);
        writer.Write(this.addressByteWritten);
        writer.Write(this.countByteRead);
        writer.Write(this.countByteWritten);
        writer.Write(this.bytesRemaining);
        writer.Write(this.bytesRemainingHighByte);
        writer.Write(this.addressHighByte);
        writer.Write(this.transferRate);

        writer.Write(this.IsMasked);
        writer.Write((int)this.TransferMode);
        writer.Write(this.Page);
        writer.Write(this.Address);
        writer.Write(this.Count);
        writer.Write(this.TransferPeriod);
        writer.Write(this.TransferChunkSize);
    }
    internal void Deserialize(BinaryReader reader)
    {
        this.isActive = reader.ReadBoolean();
        this.addressByteRead = reader.ReadBoolean();
        this.addressByteWritten = reader.ReadBoolean();
        this.countByteRead = reader.ReadBoolean();
        this.countByteWritten = reader.ReadBoolean();
        this.bytesRemaining = reader.ReadInt32();
        this.bytesRemainingHighByte = reader.ReadByte();
        this.addressHighByte = reader.ReadByte();
        this.transferRate = reader.ReadInt32();

        this.IsMasked = reader.ReadBoolean();
        this.TransferMode = (DmaTransferMode)reader.ReadInt32();
        this.Page = reader.ReadByte();
        this.Address = reader.ReadUInt16();
        this.Count = reader.ReadUInt16();
        this.TransferPeriod = reader.ReadInt64();
        this.TransferChunkSize = reader.ReadInt32();
    }

    private void OnIsActiveChanged(EventArgs e) => this.IsActiveChanged?.Invoke(this, e);
}

/// <summary>
/// Specifies the transfer mode of a DMA channel.
/// </summary>
public enum DmaTransferMode
{
    /// <summary>
    /// The DMA channel is in single-cycle mode.
    /// </summary>
    SingleCycle,
    /// <summary>
    /// The DMA channel is in auto-initialize mode.
    /// </summary>
    AutoInitialize
}
