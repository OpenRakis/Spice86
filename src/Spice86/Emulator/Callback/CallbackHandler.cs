namespace Spice86.Emulator.Callback;

using Spice86.Emulator.Errors;
using Spice86.Emulator.VM;
using Spice86.Emulator.Memory;

using System.Collections.Generic;
using System.Linq;

public class CallbackHandler : IndexBasedDispatcher<ICallback> {

    // Map of all the callback addresses
    private readonly Dictionary<byte, SegmentedAddress> _callbackAddresses = new();

    // Segment where to install the callbacks code in memory
    private readonly ushort _callbackHandlerSegment;

    private readonly Machine _machine;

    private readonly Memory _memory;

    // offset in this segment so that new callbacks are written to a fresh location
    private ushort _offset = 0;

    public CallbackHandler(Machine machine, ushort interruptHandlerSegment) {
        this._machine = machine;
        this._memory = machine.GetMemory();
        this._callbackHandlerSegment = interruptHandlerSegment;
    }

    public void AddCallback(ICallback callback) {
        AddService(callback.GetIndex(), callback);
    }

    public Dictionary<byte, SegmentedAddress> GetCallbackAddresses() {
        return _callbackAddresses;
    }

    public void InstallAllCallbacksInInterruptTable() {
        foreach (ICallback callback in _dispatchTable.Values.OrderBy(x => x.GetIndex())) {
            this.InstallCallbackInInterruptTable(callback);
        }
    }

    protected override UnhandledOperationException GenerateUnhandledOperationException(int index) {
        return new UnhandledCallbackException(_machine, index);
    }

    private void InstallCallbackInInterruptTable(ICallback callback) {
        _offset += InstallInterruptWithCallback(callback.GetIndex(), _callbackHandlerSegment, _offset);
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
        return 4;
    }
}