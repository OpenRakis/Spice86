namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Indexable;

/// <summary>
/// This is a memory based FIFO Queue used to store key codes. <br/>
/// Data about buffer start, end and positions is stored in the Bios Data Area.
/// </summary>
public class BiosKeyboardBuffer {
    private readonly IIndexable _memory;
    private readonly BiosDataArea _biosDataArea;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="biosDataArea">The memory mapped BIOS values.</param>
    public BiosKeyboardBuffer(IIndexable memory, BiosDataArea biosDataArea) {
        _memory = memory;
        _biosDataArea = biosDataArea;
    }

    /// <summary>
    /// Setups the <see cref="StartAddress"/> and <see cref="EndAddress"/> of the BIOS keyboard buffer in memory.
    /// </summary>
    internal void Init() {
        // absolute base address is uint but BDA is low in memory so it fits in ushort
        StartAddress = (ushort)_biosDataArea.KbdBuf.BaseAddress;
        EndAddress = (ushort)(StartAddress + _biosDataArea.KbdBuf.Count);
        HeadAddress = StartAddress;
        TailAddress = StartAddress;
    }

    /// <summary>
    /// Address of the start of the buffer
    /// </summary>
    private ushort StartAddress { get => _biosDataArea.KbdBufStartOffset; set => _biosDataArea.KbdBufStartOffset = value; }

    /// <summary>
    /// Address of the end of the buffer
    /// </summary>
    private ushort EndAddress { get => _biosDataArea.KbdBufEndOffset; set => _biosDataArea.KbdBufEndOffset = value; }

    /// <summary>
    /// Address where newest item is enqueued
    /// </summary>
    private ushort HeadAddress { get => _biosDataArea.KbdBufHead; set => _biosDataArea.KbdBufHead = value; }

    /// <summary>
    /// Address where the oldest item is enqueued
    /// </summary>
    private ushort TailAddress { get => _biosDataArea.KbdBufTail; set => _biosDataArea.KbdBufTail = value; }

    /// <summary>
    /// Returns whether there is any keycode in the buffer or not
    /// </summary>
    public bool IsEmpty {
        get {
            int head = HeadAddress;
            int tail = TailAddress;
            return head == tail;
        }
    }

    /// <summary>
    /// Enqueues the keycode in the buffer
    /// </summary>
    /// <param name="code">keycode to enqueue</param>
    /// <returns>false when buffer is full, true otherwise</returns>
    public bool EnqueueKeyCode(ushort code) {
        ushort newTail = ComputeNextAddress(TailAddress);
        if (newTail == HeadAddress) {
            // buffer full
            return false;
        }

        _memory.UInt16[0, TailAddress] = code;
        TailAddress = newTail;
        return true;
    }

    /// <summary>
    /// Dequeues the most recent key code from the buffer
    /// </summary>
    /// <returns>the keycode or null if buffer was empty</returns>
    public ushort? DequeueKeyCode() {
        ushort? res = PeekKeyCode();
        if (res is null) {
            // Don't dequeue if nothing in the buffer
            return null;
        }
        HeadAddress = ComputeNextAddress(HeadAddress);
        return res;
    }

    /// <summary>
    /// Peeks at the pending keycode in the BIOS keyboard buffer, and returns it without dequeuing it
    /// </summary>
    /// <returns>The pending keycode, or <c>null</c> if <see cref="IsEmpty"/> is <c>True</c>.</returns>
    public ushort? PeekKeyCode() {
        if (IsEmpty) {
            return null;
        }

        return _memory.UInt16[0, HeadAddress];
    }

    private ushort ComputeNextAddress(ushort address) {
        ushort next = (ushort)(address + 2);
        if (next >= EndAddress) {
            return StartAddress;
        }
        return next;
    }
}