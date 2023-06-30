namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.ReverseEngineer;

public class BiosKeyboardBuffer : MemoryBasedDataStructureWithBaseAddress {
    private const ushort End = 0x482;
    private const ushort Head = 0x41A;
    private const int InitialLength = 0x20;
    private const ushort InitialStartAddress = 0x41E;
    private const ushort Start = 0x480;
    private const ushort Tail = 0x41C;

    public BiosKeyboardBuffer(IMemory memory) : base(memory, 0) {
    }

    public bool AddKeyCode(ushort code) {
        ushort tail = TailAddress;
        ushort newTail = AdvancePointer(tail);
        if (newTail == HeadAddress) {
            // buffer full
            return false;
        }

        SetUint16(tail, code);
        TailAddress = newTail;
        return true;
    }

    public bool IsEmpty {
        get {
            int head = HeadAddress;
            int tail = TailAddress;
            return head == tail;
        }
    }

    public ushort EndAddress { get => GetUint16(End); set => SetUint16(End, value); }

    public ushort HeadAddress { get => GetUint16(Head); set => SetUint16(Head, value); }

    public ushort? GetKeyCodeStatus() {
        ushort head = HeadAddress;
        if (IsEmpty) {
            return null;
        }

        return GetUint16(head);
    }

    public ushort? GetKeyCode() {
        ushort head = HeadAddress;
        if (IsEmpty) {
            return null;
        }

        HeadAddress = AdvancePointer(HeadAddress);
        return GetUint16(head);
    }

    public ushort StartAddress { get => GetUint16(Start); set => SetUint16(Start, value); }

    public ushort TailAddress { get => GetUint16(Tail); set => SetUint16(Tail, value); }

    public void Init() {
        StartAddress = InitialStartAddress;
        EndAddress = InitialStartAddress + InitialLength;
        HeadAddress = InitialStartAddress;
        TailAddress = InitialStartAddress;
    }

    private ushort AdvancePointer(ushort value) {
        ushort res = (ushort)(value + 2);
        if (res >= EndAddress) {
            return StartAddress;
        }

        return res;
    }
}