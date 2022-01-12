using Spice86.Emulator.Errors;

using System.Collections.Generic;
using System.Linq;

namespace Spice86.Emulator.Callback;

using Spice86.Emulator.Machine;
using Spice86.Emulator.Memory;

public class CallbackHandler : IndexBasedDispatcher<ICallback>
{
    // Map of all the callback addresses
    private readonly Dictionary<int, SegmentedAddress> _callbackAddresses = new();

    // Segment where to install the callbacks code in memory
    private readonly ushort _callbackHandlerSegment;

    private readonly Machine _machine;

    private readonly Memory? _memory;

    // offset in this segment so that new callbacks are written to a fresh location
    private ushort _offset = 0;

    public CallbackHandler(Machine machine, ushort interruptHandlerSegment)
    {
        this._machine = machine;
        this._memory = machine.GetMemory();
        this._callbackHandlerSegment = interruptHandlerSegment;
    }

    public void AddCallback(ICallback callback)
    {
        AddService(callback.GetIndex(), callback);
    }

    public Dictionary<int, SegmentedAddress> GetCallbackAddresses()
    {
        return _callbackAddresses;
    }

    public void InstallAllCallbacksInInterruptTable()
    {
        foreach (var callback in _dispatchTable.Values.OrderBy(x => x.GetIndex()))
        {
            this.InstallCallbackInInterruptTable(callback);
        }
    }

    protected override UnhandledOperationException GenerateUnhandledOperationException(int index)
    {
        return new UnhandledCallbackException(_machine, index);
    }

    private void InstallCallbackInInterruptTable(ICallback callback)
    {
        _offset += InstallInterruptWithCallback(callback.GetIndex(), _callbackHandlerSegment, _offset);
    }

    private ushort InstallInterruptWithCallback(int vectorNumber, ushort segment, ushort offset)
    {
        InstallVectorInTable(vectorNumber, segment, offset);
        return WriteInterruptCallback(vectorNumber, segment, offset);
    }

    private void InstallVectorInTable(int vectorNumber, ushort segment, ushort offset)
    {
        // install the vector in the vector table
        _memory?.SetUint16(4 * vectorNumber + 2, segment);
        _memory?.SetUint16(4 * vectorNumber, offset);
    }

    private ushort WriteInterruptCallback(int vectorNumber, int segment, int offset)
    {
        _callbackAddresses.Add(vectorNumber, new SegmentedAddress(segment, offset));
        int address = MemoryUtils.ToPhysicalAddress(segment, offset);

        // CALLBACK opcode (custom instruction, FE38 + 16 bits callback number)
        _memory?.SetUint8(address, 0xFE);
        _memory?.SetUint8(address + 1, 0x38);

        // vector to call
        _memory?.SetUint16(address + 2, (ushort)vectorNumber);

        // IRET
        _memory?.SetUint8(address + 4, 0xCF);

        // 5 bytes used
        return 5;
    }
}