using Ix86.Emulator.Errors;

using System.Collections.Generic;
using System.Linq;

namespace Ix86.Emulator.Callback;
using Ix86.Emulator.Machine;
using Ix86.Emulator.Memory;

public class CallbackHandler : IndexBasedDispatcher<ICallback>
{
    private Machine _machine;
    private Memory? _memory;
    // Segment where to install the callbacks code in memory
    private int _callbackHandlerSegment;
    // offset in this segment so that new callbacks are written to a fresh location
    private int _offset = 0;
    // Map of all the callback addresses
    private readonly Dictionary<int, SegmentedAddress> _callbackAddresses = new();
    public CallbackHandler(Machine machine, int interruptHandlerSegment)
    {
        this._machine = machine;
        this._memory = machine.GetMemory();
        this._callbackHandlerSegment = interruptHandlerSegment;
    }

    public virtual void AddCallback(ICallback callback)
    {
        AddService(callback.GetIndex(), callback);
    }

    public virtual Dictionary<int, SegmentedAddress> GetCallbackAddresses()
    {
        return _callbackAddresses;
    }

    protected override UnhandledOperationException GenerateUnhandledOperationException(int index)
    {
        return new UnhandledCallbackException(_machine, index);
    }

    public virtual void InstallAllCallbacksInInterruptTable()
    {
        foreach (var callback in _dispatchTable.Values.OrderBy(x => x.GetIndex()))
        {
            this.InstallCallbackInInterruptTable(callback);
        }
    }

    private void InstallCallbackInInterruptTable(ICallback callback)
    {
        _offset += InstallInterruptWithCallback(callback.GetIndex(), _callbackHandlerSegment, _offset);
    }

    private int InstallInterruptWithCallback(int vectorNumber, int segment, int offset)
    {
        InstallVectorInTable(vectorNumber, segment, offset);
        return WriteInterruptCallback(vectorNumber, segment, offset);
    }

    private int WriteInterruptCallback(int vectorNumber, int segment, int offset)
    {
        _callbackAddresses.Add(vectorNumber, new SegmentedAddress(segment, offset));
        int address = MemoryUtils.ToPhysicalAddress(segment, offset);

        // CALLBACK opcode (custom instruction, FE38 + 16 bits callback number)
        _memory?.SetUint8(address, 0xFE);
        _memory?.SetUint8(address + 1, 0x38);

        // vector to call
        _memory?.SetUint16(address + 2, vectorNumber);

        // IRET
        _memory?.SetUint8(address + 4, 0xCF);

        // 5 bytes used
        return 5;
    }

    private void InstallVectorInTable(int vectorNumber, int segment, int offset)
    {

        // install the vector in the vector table
        _memory?.SetUint16(4 * vectorNumber + 2, segment);
        _memory?.SetUint16(4 * vectorNumber, offset);
    }
}
