namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

/// <summary>
/// This is a memory based FIFO Queue used to store key codes
/// </summary>
public class BiosKeyboardBuffer : MemoryBasedDataStructure {
    private const ushort End = 0x482;
    private const ushort Head = 0x41A;
    private const int InitialLength = 0x20;
    private const ushort InitialStartAddress = 0x41E;
    private const ushort Start = 0x480;
    private const ushort Tail = 0x41C;

    public BiosKeyboardBuffer(Memory memory) : base(memory, 0) {
    }

    public void Init() {
        StartAddress = InitialStartAddress;
        EndAddress = InitialStartAddress + InitialLength;
        HeadAddress = InitialStartAddress;
        TailAddress = InitialStartAddress;
    }

    /// <summary>
    /// Address of the start of the buffer
    /// </summary>
    public ushort StartAddress { get => UInt16[Start]; set => UInt16[Start] = value; }

    /// <summary>
    /// Address of the end of the buffer
    /// </summary>
    public ushort EndAddress { get => UInt16[End]; set => UInt16[End] = value; }

    /// <summary>
    /// Address where newest item is enqueued
    /// </summary>
    public ushort HeadAddress { get => UInt16[Head]; set => UInt16[Head] = value; }

    /// <summary>
    /// Address where the oldest item is enqueued
    /// </summary>
    public ushort TailAddress { get => UInt16[Tail]; set => UInt16[Tail] = value; }

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

        UInt16[tail] = code;
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

        return UInt16[HeadAddress];
    }

    private ushort AdvancePointer(ushort value) {
        ushort res = (ushort)(value + 2);
        if (res >= EndAddress) {
            return StartAddress;
        }
        return res;
    }
}