namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// Helper to get function arguments from the stack.
/// </summary>
public class ArgumentFetcher {
    private readonly Stack _stack;
    private readonly IMemory _memory;
    private readonly State _state;

    /// <summary>
    /// Instantiates a new instance.
    /// </summary>
    /// <param name="cpu"></param>
    /// <param name="memory"></param>
    public ArgumentFetcher(Cpu cpu, IMemory memory) {
        _stack = cpu.Stack;
        _memory = memory;
        _state = cpu.State;
    }

    public void Get(out ushort arg1, out uint arg2, out ushort arg3) {
        arg1 = _stack.Peek16(4);
        arg2 = _stack.Peek32(6);
        arg3 = _stack.Peek16(10);
    }

    public void Get(out string arg1, out short arg2, out ushort arg3) {
        ushort dosPathPointerOffset = _stack.Peek16(4);
        arg2 = (short)_stack.Peek16(6);
        arg3 = _stack.Peek16(8);
        arg1 = GetStringFromDsPointer(dosPathPointerOffset);
    }

    public void Get(out string arg1, out ushort arg2, out short arg3) {
        ushort dosPathPointerOffset = _stack.Peek16(4);
        arg2 = _stack.Peek16(6);
        arg3 = (short)_stack.Peek16(8);
        arg1 = GetStringFromDsPointer(dosPathPointerOffset);
    }

    public void Get(out ushort arg1, out int arg2, out ushort arg3) {
        arg1 = _stack.Peek16(4);
        arg2 = (int)_stack.Peek32(6);
        arg3 = _stack.Peek16(10);
    }

    public void Get(out ushort arg1, out ushort arg2) {
        arg1 = _stack.Peek16(4);
        arg2 = _stack.Peek16(6);
    }

    public void Get(out ushort arg1) {
        arg1 = _stack.Peek16(4);
    }

    public void Get(out string arg1) {
        ushort arg1PointerOffset = _stack.Peek16(4);
        arg1 = GetStringFromDsPointer(arg1PointerOffset);
    }

    public void Get(out string arg1, out string arg2) {
        ushort arg1PointerOffset = _stack.Peek16(4);
        ushort arg2PointerOffset = _stack.Peek16(6);
        arg1 = GetStringFromDsPointer(arg1PointerOffset);
        arg2 = GetStringFromDsPointer(arg2PointerOffset);
    }

    public void Get(out ushort arg1, out ushort arg2, out ushort arg3, out ushort arg4) {
        arg1 = _stack.Peek16(4);
        arg2 = _stack.Peek16(6);
        arg3 = _stack.Peek16(8);
        arg4 = _stack.Peek16(10);
    }

    private string GetStringFromDsPointer(ushort offset) {
        uint address = MemoryUtils.ToPhysicalAddress(_state.DS, offset);
        return _memory.GetZeroTerminatedString(address, int.MaxValue);
    }

    public void Get(out ushort arg1, out string arg2) {
        arg1 = _stack.Peek16(4);
        ushort arg2PointerOffset = _stack.Peek16(6);
        arg2 = GetStringFromDsPointer(arg2PointerOffset);
    }

    public void Get(out ushort arg1, out ushort arg2, out ushort arg3) {
        arg1 = _stack.Peek16(4);
        arg2 = _stack.Peek16(6);
        arg3 = _stack.Peek16(8);
    }

    public void Get(out uint arg1, out uint arg2, out ushort arg3) {
        arg1 = _stack.Peek32(4);
        arg2 = _stack.Peek32(8);
        arg3 = _stack.Peek16(12);
    }
}