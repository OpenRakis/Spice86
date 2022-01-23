namespace Spice86.Emulator.InterruptHandlers.Input.Keyboard;

using Spice86.Emulator.Memory;
using Spice86.Emulator.ReverseEngineer;

public class BiosKeyboardBuffer : MemoryBasedDataStructureWithBaseAddress {
    private static readonly ushort END = 0x482;
    private static readonly ushort HEAD = 0x41A;
    private static readonly int INITIAL_LENGTH = 0x20;
    private static readonly ushort INITIAL_START_ADDRESS = 0x41E;
    private static readonly ushort START = 0x480;
    private static readonly ushort TAIL = 0x41C;

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
        return this.GetUint16(END);
    }

    public ushort GetHeadAddress() {
        return this.GetUint16(HEAD);
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
        return this.GetUint16(START);
    }

    public ushort GetTailAddress() {
        return this.GetUint16(TAIL);
    }

    public void Init() {
        this.SetStartAddress(INITIAL_START_ADDRESS);
        this.SetEndAddress((ushort)(INITIAL_START_ADDRESS + INITIAL_LENGTH));
        this.SetHeadAddress(INITIAL_START_ADDRESS);
        this.SetTailAddress(INITIAL_START_ADDRESS);
    }

    public void SetEndAddress(ushort value) {
        this.SetUint16(END, value);
    }

    public void SetHeadAddress(ushort value) {
        this.SetUint16(HEAD, value);
    }

    public void SetStartAddress(ushort value) {
        this.SetUint16(START, value);
    }

    public void SetTailAddress(ushort value) {
        this.SetUint16(TAIL, value);
    }

    private ushort AdvancePointer(ushort value) {
        ushort res = (ushort)(value + 2);
        if (res >= GetEndAddress()) {
            return GetStartAddress();
        }

        return res;
    }
}