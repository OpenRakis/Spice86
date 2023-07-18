namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Indexable;

/// <summary>
/// This is a memory based FIFO Queue used to store key codes
/// Data about buffer start, end and positions are stored in the Bios Data Area.
/// </summary>
public class BiosKeyboardBuffer {
    private readonly Indexable _indexable;
    private readonly BiosDataArea _biosDataArea;

    public BiosKeyboardBuffer(Indexable indexable, BiosDataArea biosDataArea) {
        _indexable = indexable;
        _biosDataArea = biosDataArea;
    }

    public void Init() {
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
        ushort tail = TailAddress;
        ushort newTail = AdvancePointer(tail);
        if (newTail == HeadAddress) {
            // buffer full
            return false;
        }

        _indexable.UInt16[0, tail] = code;
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
        HeadAddress = AdvancePointer(HeadAddress);
        return res;
    }

    /// <summary>
    /// Peek the keycode without dequeuing it
    /// </summary>
    /// <returns></returns>
    public ushort? PeekKeyCode() {
        if (IsEmpty) {
            return null;
        }

        return _indexable.UInt16[0, HeadAddress];
    }

    private ushort AdvancePointer(ushort value) {
        ushort res = (ushort)(value + 2);
        if (res >= EndAddress) {
            return StartAddress;
        }
        return res;
    }
}