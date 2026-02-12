namespace Spice86.Core.Emulator.Devices.DirectMemoryAccess;

using Serilog.Events;

using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.Diagnostics;

public sealed class DmaChannel {
    /// <summary>
    ///     Callback invoked when the channel changes state.
    /// </summary>
    /// <param name="channel">The DMA channel raising the event.</param>
    /// <param name="dmaEvent">The emitted DMA event.</param>
    public delegate void DmaCallback(DmaChannel channel, DmaEvent dmaEvent);

    /// <summary>
    ///     Callback invoked to evict a previously registered reservation owner.
    /// </summary>
    public delegate void DmaEvictCallback();

    /// <summary>
    ///     Describes the events emitted by the DMA channel callback.
    /// </summary>
    public enum DmaEvent {
        ReachedTerminalCount,
        IsMasked,
        IsUnmasked
    }

    private readonly ILoggerService _logger;

    /// <summary>
    ///     Backing store representing the addressable DMA memory space.
    /// </summary>
    private readonly IMemory _memory;

    /// <summary>
    ///     Mask applied to DMA addresses to emulate the hardware's wrapping behavior.
    /// </summary>
    private readonly uint _wrappingMask;

    internal readonly byte ShiftCount;

    /// <summary>
    ///     Registered observer notified whenever the channel changes state.
    /// </summary>
    private DmaCallback? _callback;

    /// <summary>
    ///     Callback used to inform a previous reservation owner that it lost control over the channel.
    /// </summary>
    private DmaEvictCallback? _evictCallback;

    /// <summary>
    ///     Human-readable label of the component currently holding the reservation, if any.
    /// </summary>
    private string? _reservationOwnerName;

    /// <summary>
    ///     Reload value for <see cref="CurrentAddress" /> when auto-initialization is enabled.
    /// </summary>
    internal ushort BaseAddress;

    /// <summary>
    ///     Reload value for <see cref="CurrentCount" /> when auto-initialization is enabled.
    /// </summary>
    internal ushort BaseCount;

    /// <summary>
    ///     Word address relative to <see cref="PageBase" /> that will be accessed next.
    /// </summary>
    internal uint CurrentAddress;

    /// <summary>
    ///     Remaining number of words to transfer for the current DMA request.
    /// </summary>
    internal ushort CurrentCount;

    /// <summary>
    ///     Tracks whether the device currently asserts its DMA request line.
    /// </summary>
    internal bool HasRaisedRequest;

    /// <summary>
    ///     Tracks whether the last transfer reached terminal count.
    /// </summary>
    internal bool HasReachedTerminalCount;

    /// <summary>
    ///     When true, the controller reloads base values after reaching terminal count.
    /// </summary>
    internal bool IsAutoiniting;

    /// <summary>
    ///     Determines whether the DMA address increments (true) or decrements (false) after each word.
    /// </summary>
    internal bool IsIncremented = true;

    /// <summary>
    ///     True when the channel is masked and therefore not eligible for transfers.
    /// </summary>
    internal bool IsMasked = true;

    /// <summary>
    ///     Base physical address calculated from the page register for the current transfer.
    /// </summary>
    internal uint PageBase;

    /// <summary>
    ///     Raw eight-bit value written to the DMA page register.
    /// </summary>
    internal byte PageRegisterValue;

    /// <summary>
    ///     Constructs a DMA channel with the supplied number and width, binding it to memory and logging services.
    /// </summary>
    public DmaChannel(byte num, bool dma16Bit, IMemory memory, ILoggerService logger, uint wrappingMask = 0xFFFF) {
        ChannelNumber = num;
        ShiftCount = dma16Bit ? (byte)0x1 : (byte)0x0;
        _memory = memory;
        _logger = logger;
        _wrappingMask = wrappingMask;

        _logger.Debug(
            "DMA[{Channel}]: Constructed {Width}-bit channel with wrapping mask 0x{Mask:X}",
            ChannelNumber,
            Is16Bit ? 16 : 8,
            _wrappingMask);

        if (num == 4) {
            return;
        }

        Debug.Assert(IsMasked);
    }

    /// <summary>
    ///     Indicates whether the channel transfers 16-bit words or 8-bit bytes.
    /// </summary>
    internal bool Is16Bit => ShiftCount == 1;

    /// <summary>
    ///     Zero-based identifier of this DMA channel.
    /// </summary>
    public byte ChannelNumber { get; }

    /// <summary>
    ///     Releases any outstanding reservation when the channel is finalized.
    /// </summary>
    ~DmaChannel() {
        if (!HasReservation()) {
            return;
        }

        _logger.Debug("DMA: Shutting down {Owner} on {Width}-bit DMA channel {Channel}", _reservationOwnerName,
            Is16Bit ? 16 : 8, ChannelNumber);

        EvictReserver();
    }

    /// <summary>
    ///     Notifies listeners about a change in the channel state.
    /// </summary>
    private void DoCallback(DmaEvent dmaEvent) {
        _logger.Verbose("DMA[{Channel}]: Emitting {Event} event", ChannelNumber, dmaEvent);
        _callback?.Invoke(this, dmaEvent);
    }

    /// <summary>
    ///     Applies the mask bit controlling whether the channel is allowed to perform transfers.
    /// </summary>
    public void SetMask(bool mask) {
        IsMasked = mask;
        _logger.Debug("DMA[{Channel}]: Mask set to {Masked}", ChannelNumber, mask);
        DoCallback(IsMasked ? DmaEvent.IsMasked : DmaEvent.IsUnmasked);
    }

    /// <summary>
    ///     Registers a callback to observe channel events, updating the request line accordingly.
    /// </summary>
    public void RegisterCallback(DmaCallback? callback) {
        _callback = callback;
        SetMask(IsMasked);
        if (_callback is not null) {
            RaiseRequest();
        } else {
            ClearRequest();
        }

        _logger.Debug("DMA[{Channel}]: Callback {State}", ChannelNumber, _callback != null ? "registered" : "cleared");
    }

    /// <summary>
    ///     Sets the terminal-count flag and informs listeners that the programmed length was completed.
    /// </summary>
    private void ReachedTerminalCount() {
        HasReachedTerminalCount = true;
        _logger.Debug("DMA[{Channel}]: Reached terminal count (auto-init {AutoInit})", ChannelNumber,
            IsAutoiniting);

        DoCallback(DmaEvent.ReachedTerminalCount);
    }

    /// <summary>
    ///     Updates the page register and recomputes the base physical address accordingly.
    /// </summary>
    public void SetPage(byte value) {
        PageRegisterValue = value;
        PageBase = ((uint)PageRegisterValue >> ShiftCount) << (16 + ShiftCount);
        _logger.Debug("DMA[{Channel}]: Page register set to 0x{Page:X2} (base 0x{Base:X6})", ChannelNumber,
            PageRegisterValue,
            PageBase);
    }

    /// <summary>
    ///     Marks the request line as asserted by the attached device.
    /// </summary>
    private void RaiseRequest() {
        HasRaisedRequest = true;
        _logger.Verbose("DMA[{Channel}]: Request line asserted", ChannelNumber);
    }

    /// <summary>
    ///     Clears the request line, signaling the transfer source no longer needs service.
    /// </summary>
    internal void ClearRequest() {
        HasRaisedRequest = false;
        _logger.Verbose("DMA[{Channel}]: Request line cleared", ChannelNumber);
    }

    /// <summary>
    ///     Copies words from memory into <paramref name="destinationBuffer" />.
    /// </summary>
    public int Read(int words, Span<byte> destinationBuffer) {
        int bytesRequested = words << ShiftCount;
        if (destinationBuffer.Length < bytesRequested) {
            throw new ArgumentException($"Destination buffer too small for DMA read of {bytesRequested} bytes.",
                nameof(destinationBuffer));
        }

        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose(
                "DMA[{Channel}]: Read scheduled for {Words} words ({Bytes} bytes) into buffer length {BufferLength}",
                ChannelNumber,
                words,
                bytesRequested,
                destinationBuffer.Length);
        }

        return ReadOrWrite(DmaDirection.Read, words, destinationBuffer, ReadOnlySpan<byte>.Empty);
    }

    /// <summary>
    ///     Writes words from <paramref name="sourceBuffer" /> into memory.
    /// </summary>
    public int Write(int words, ReadOnlySpan<byte> sourceBuffer) {
        int bytesRequested = words << ShiftCount;
        if (sourceBuffer.Length < bytesRequested) {
            throw new ArgumentException($"Source buffer too small for DMA write of {bytesRequested} bytes.",
                nameof(sourceBuffer));
        }

        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose(
                "DMA[{Channel}]: Write scheduled for {Words} words ({Bytes} bytes) from buffer length {BufferLength}",
                ChannelNumber,
                words,
                bytesRequested,
                sourceBuffer.Length);
        }

        return ReadOrWrite(DmaDirection.Write, words, Span<byte>.Empty, sourceBuffer);
    }

    /// <summary>
    ///     Resets all controller registers and clears callbacks and reservations.
    /// </summary>
    public void Reset() {
        PageBase = 0;
        CurrentAddress = 0;

        BaseAddress = 0;
        BaseCount = 0;
        CurrentCount = 0;

        PageRegisterValue = 0;

        IsIncremented = true;
        IsAutoiniting = false;
        IsMasked = true;
        HasReachedTerminalCount = false;
        HasRaisedRequest = false;

        _callback = null;
        _evictCallback = null;
        _reservationOwnerName = null;

        _logger.Debug("DMA[{Channel}]: Registers reset to defaults", ChannelNumber);
    }

    /// <summary>
    ///     Reserves the channel for an owning component, evicting any previous owner.
    /// </summary>
    public void ReserveFor(string ownerName, DmaEvictCallback evictCallback) {
        ArgumentNullException.ThrowIfNull(ownerName);
        ArgumentNullException.ThrowIfNull(evictCallback);

        if (HasReservation()) {
            if (_logger.IsEnabled(LogEventLevel.Debug)) {
                _logger.Debug("DMA: {Owner} is replacing {PreviousOwner} on {Width}-bit DMA channel {Channel}",
                    ownerName,
                    _reservationOwnerName,
                    Is16Bit ? 16 : 8,
                    ChannelNumber);
            }

            EvictReserver();
        }

        Reset();
        _evictCallback = evictCallback;
        _reservationOwnerName = ownerName;

        _logger.Debug("DMA[{Channel}]: Reserved for {Owner}", ChannelNumber, ownerName);
    }

    /// <summary>
    ///     Implements shared transfer logic for reads and writes, updating address and count registers.
    /// </summary>
    private int ReadOrWrite(DmaDirection direction, int words, Span<byte> readBuffer, ReadOnlySpan<byte> writeBuffer) {
        switch (words) {
            case < 0:
                _logger.Warning(
                    "DMA[{Channel}]: Requested transfer with negative word count {Words}; treating as zero",
                    ChannelNumber,
                    words);
                break;
            case > ushort.MaxValue:
                _logger.Warning(
                    "DMA[{Channel}]: Requested transfer of {Words} words exceeds counter width; clamping to {MaxWords}",
                    ChannelNumber,
                    words,
                    ushort.MaxValue);
                break;
        }

        unchecked {
            // DMA math must not raise an OverFlowException to simulate the ISA hardare
            ushort want = (ushort)Math.Clamp(words, 0, ushort.MaxValue);
            ushort done = 0;
            CurrentAddress &= _wrappingMask;

            int bufferOffsetBytes = 0;
            while (want > 0) {
                uint left = (uint)(CurrentCount + 1);
                if (want < left) {
                    if (direction == DmaDirection.Read) {
                        PerformRead(PageBase, CurrentAddress, want, readBuffer, IsIncremented, bufferOffsetBytes);
                    } else {
                        PerformWrite(PageBase, CurrentAddress, want, writeBuffer, IsIncremented, bufferOffsetBytes);
                    }
                    
                    done += want;
                    CurrentAddress = IsIncremented
                        ? unchecked(CurrentAddress + want)
                        : unchecked(CurrentAddress - want);
                    CurrentCount -= want;
                    want = 0;
                } else {
                    if (direction == DmaDirection.Read) {
                        PerformRead(PageBase, CurrentAddress, left, readBuffer, IsIncremented, bufferOffsetBytes);
                    } else {
                        PerformWrite(PageBase, CurrentAddress, left, writeBuffer, IsIncremented, bufferOffsetBytes);
                    }

                    bufferOffsetBytes += (int)(left << ShiftCount);
                    want -= (ushort)left;
                    done += (ushort)left;
                    ReachedTerminalCount();

                    if (IsAutoiniting) {
                        CurrentCount = BaseCount;
                        CurrentAddress = BaseAddress;
                        continue;
                    }

                    CurrentAddress = IsIncremented
                        ? unchecked(CurrentAddress + left)
                        : unchecked(CurrentAddress - left);
                    CurrentCount = 0xFFFF;
                    SetMask(true);
                    break;
                }
            }

            return done;
        }
    }

    private uint BytesPerWord => 1u << ShiftCount;

    private void PerformRead(uint pageBase, uint dmaAddress, uint words, Span<byte> readBuffer, bool isIncremented,
        int bufferIndex) {
        uint bytesPerWord = BytesPerWord;
        for (uint wordIndex = 0; wordIndex < words; wordIndex++) {
            uint dmaWordAddress = isIncremented
                ? unchecked(dmaAddress + wordIndex)
                : unchecked(dmaAddress - wordIndex);
            uint maskedDmaAddress = dmaWordAddress & _wrappingMask;
            uint baseAddress = unchecked(pageBase + (maskedDmaAddress << ShiftCount));

            for (uint byteIndex = 0; byteIndex < bytesPerWord; byteIndex++) {
                uint address = unchecked(baseAddress + byteIndex);
                readBuffer[bufferIndex] = _memory[address];
                bufferIndex++;
            }
        }
    }

    private void PerformWrite(uint pageBase, uint dmaAddress, uint words, ReadOnlySpan<byte> writeBuffer,
        bool isIncremented, int bufferIndex) {
        uint bytesPerWord = BytesPerWord;
        for (uint wordIndex = 0; wordIndex < words; wordIndex++) {
            uint dmaWordAddress = isIncremented
                ? unchecked(dmaAddress + wordIndex)
                : unchecked(dmaAddress - wordIndex);
            uint maskedDmaAddress = dmaWordAddress & _wrappingMask;
            uint baseAddress = unchecked(pageBase + (maskedDmaAddress << ShiftCount));

            for (uint byteIndex = 0; byteIndex < bytesPerWord; byteIndex++) {
                uint address = unchecked(baseAddress + byteIndex);
                _memory[address] = writeBuffer[bufferIndex];
                bufferIndex++;
            }
        }
    }

    /// <summary>
    ///     Indicates whether the channel is currently owned by a component.
    /// </summary>
    private bool HasReservation() {
        return _evictCallback is not null && !string.IsNullOrEmpty(_reservationOwnerName);
    }

    /// <summary>
    ///     Notifies the current owner that its reservation ended and clears the bookkeeping fields.
    /// </summary>
    private void EvictReserver() {
        Debug.Assert(HasReservation());
        _evictCallback?.Invoke();
        _logger.Debug("DMA[{Channel}]: Reservation evicted (previous owner {Owner})", ChannelNumber,
            _reservationOwnerName);

        _evictCallback = null;
        _reservationOwnerName = null;
    }

    public void SetMode(byte value) {
        IsAutoiniting = (value & 0x10) != 0;
        IsIncremented = (value & 0x20) == 0;
    }

    /// <summary>
    ///     Describes DMA transfer direction.
    /// </summary>
    private enum DmaDirection {
        Read,
        Write
    }
}