namespace Spice86.Emulator.InterruptHandlers.Input.Keyboard;

using Spice86.Emulator.Memory;
using Spice86.Emulator.ReverseEngineer;

public class BiosKeyboardBuffer : MemoryBasedDataStructureWithBaseAddress {
    private const ushort End = 0x482;
    private const ushort Head = 0x41A;
    private const int InitialLength = 0x20;
    private const ushort InitialStartAddress = 0x41E;
    private const ushort Start = 0x480;
    private const ushort Tail = 0x41C;

    public BiosKeyboardBuffer(Memory memory) : base(memory, 0) {
    }

    public bool AddKeyCode(ushort code) {
        ushort tail = TailAddress;
        ushort newTail = AdvancePointer(tail);
        if (newTail == HeadAddress) {
            // buffer full
            return false;
        }

        this.SetUint16(tail, code);
        this.TailAddress = newTail;
        return true;
    }

    public bool IsEmpty {
        get {
            int head = HeadAddress;
            int tail = TailAddress;
            return head == tail;
        }
    }

    public ushort EndAddress { get => this.GetUint16(End); set => this.SetUint16(End, value); }

    public ushort HeadAddress { get => this.GetUint16(Head); set => this.SetUint16(Head, value); }

    public ushort? GetKeyCode() {
        ushort head = HeadAddress;
        if (IsEmpty) {
            return null;
        }

        ushort newHead = AdvancePointer(HeadAddress);
        HeadAddress = newHead;
        return this.GetUint16(head);
    }

    public ushort StartAddress { get => this.GetUint16(Start); set => this.SetUint16(Start, value); }

    public ushort TailAddress { get => this.GetUint16(Tail); set => this.SetUint16(Tail, value); }

    public void Init() {
        this.StartAddress = InitialStartAddress;
        this.EndAddress = (ushort)(InitialStartAddress + InitialLength);
        this.HeadAddress = InitialStartAddress;
        this.TailAddress = InitialStartAddress;
    }

    private ushort AdvancePointer(ushort value) {
        ushort res = (ushort)(value + 2);
        if (res >= EndAddress) {
            return StartAddress;
        }

        return res;
    }
}