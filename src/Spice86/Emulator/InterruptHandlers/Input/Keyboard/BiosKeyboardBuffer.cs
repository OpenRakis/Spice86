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
        ushort tail = GetTailAddress();
        ushort newTail = AdvancePointer(tail);
        if (newTail == GetHeadAddress()) {
            // buffer full
            return false;
        }

        this.SetUint16(tail, code);
        this.SetTailAddress(newTail);
        return true;
    }

    public bool Empty() {
        int head = GetHeadAddress();
        int tail = GetTailAddress();
        return head == tail;
    }

    public ushort GetEndAddress() {
        return this.GetUint16(End);
    }

    public ushort GetHeadAddress() {
        return this.GetUint16(Head);
    }

    public ushort? GetKeyCode() {
        ushort head = GetHeadAddress();
        if (Empty()) {
            return null;
        }

        ushort newHead = AdvancePointer(GetHeadAddress());
        this.SetHeadAddress(newHead);
        return this.GetUint16(head);
    }

    public ushort GetStartAddress() {
        return this.GetUint16(Start);
    }

    public ushort GetTailAddress() {
        return this.GetUint16(Tail);
    }

    public void Init() {
        this.SetStartAddress(InitialStartAddress);
        this.SetEndAddress((ushort)(InitialStartAddress + InitialLength));
        this.SetHeadAddress(InitialStartAddress);
        this.SetTailAddress(InitialStartAddress);
    }

    public void SetEndAddress(ushort value) {
        this.SetUint16(End, value);
    }

    public void SetHeadAddress(ushort value) {
        this.SetUint16(Head, value);
    }

    public void SetStartAddress(ushort value) {
        this.SetUint16(Start, value);
    }

    public void SetTailAddress(ushort value) {
        this.SetUint16(Tail, value);
    }

    private ushort AdvancePointer(ushort value) {
        ushort res = (ushort)(value + 2);
        if (res >= GetEndAddress()) {
            return GetStartAddress();
        }

        return res;
    }
}