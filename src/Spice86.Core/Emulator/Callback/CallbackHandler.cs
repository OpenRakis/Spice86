
namespace Spice86.Core.Emulator.Callback;

using System.Collections.Generic;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;

public class CallbackHandler : IndexBasedDispatcher {
    private static readonly ushort CallbackSize = 4;

    // Map of all the callback addresses
    private readonly Dictionary<byte, SegmentedAddress> _callbackAddresses = new();

    // Segment where to install the callbacks code in memory
    private readonly ushort _callbackHandlerSegment;

    // offset in this segment so that new callbacks are written to a fresh location
    private ushort _offset = 0;

    private readonly Machine _machine;

    private readonly Memory _memory;

    public CallbackHandler(Machine machine, ushort interruptHandlerSegment) {
        _machine = machine;
        _memory = machine.Memory;
        _callbackHandlerSegment = interruptHandlerSegment;
    }

    public void AddCallback(ICallback callback) {
        AddService(callback.Index, callback);
    }

    public Dictionary<byte, SegmentedAddress> GetCallbackAddresses() {
        return _callbackAddresses;
    }

    public void InstallAllCallbacksInInterruptTable() {
        foreach (ICallback callback in _dispatchTable.Values) {
            InstallCallbackInInterruptTable(callback);
        }
    }

    /// <summary>
    /// Returns a copy of the RAM with all the callback instructions replaced by NOPs.
    /// This is to make the RAM dumps loadable with ghidra.
    /// </summary>
    /// <returns></returns>
    public byte[] NopCallbackInstructionInRamCopy() {
        byte[] res = (byte[])_memory.Ram.Clone();
        for(int i=0;i<_dispatchTable.Values.Count;i++) {
            uint ramIndex = MemoryUtils.ToPhysicalAddress(_callbackHandlerSegment, (ushort)(i * CallbackSize));
            for (int j = 0; j < CallbackSize - 1; j++) {
                // NOP the callback except for the IRET
                res[ramIndex + j] = 0x90;
            }
        }
        return res;
    }

    protected override UnhandledOperationException GenerateUnhandledOperationException(int index) {
        return new UnhandledCallbackException(_machine, index);
    }

    private void InstallCallbackInInterruptTable(ICallback callback) {
        _offset += InstallInterruptWithCallback(callback.Index, _callbackHandlerSegment, _offset);
    }

    private ushort InstallInterruptWithCallback(byte vectorNumber, ushort segment, ushort offset) {
        InstallVectorInTable(vectorNumber, segment, offset);
        return WriteInterruptCallback(vectorNumber, segment, offset);
    }

    private void InstallVectorInTable(byte vectorNumber, ushort segment, ushort offset) {
        // install the vector in the vector table
        _memory.SetUint16((ushort)(4 * vectorNumber + 2), segment);
        _memory.SetUint16((ushort)(4 * vectorNumber), offset);
    }

    private ushort WriteInterruptCallback(byte vectorNumber, ushort segment, ushort offset) {
        _callbackAddresses.Add(vectorNumber, new SegmentedAddress(segment, offset));
        uint address = MemoryUtils.ToPhysicalAddress(segment, offset);

        // CALLBACK opcode (custom instruction, FE38 + 16 bits callback number)
        _memory.SetUint8(address, 0xFE);
        _memory.SetUint8(address + 1, 0x38);

        // vector to call
        _memory.SetUint8(address + 2, vectorNumber);

        // IRET
        _memory.SetUint8(address + 3, 0xCF);

        // 4 bytes used
        return CallbackSize;
    }
}