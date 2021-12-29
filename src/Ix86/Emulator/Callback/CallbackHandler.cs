//using Ix86.Emulator.Errors;
//using Ix86.Emulator.Memory;

//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

//namespace Ix86.Emulator.Callback;
//using Ix86.Emulator.Machine;
//using Ix86.Emulator.Memory;

//public class CallbackHandler : IndexBasedDispatcher<ICallback>
//{
//    private Machine machine;
//    private Memory memory;
//    // Segment where to install the callbacks code in memory
//    private int callbackHandlerSegment;
//    // offset in this segment so that new callbacks are written to a fresh location
//    private int offset = 0;
//    // Map of all the callback addresses
//    private Dictionary<int, SegmentedAddress> callbackAddresses = new();
//    public CallbackHandler(Machine machine, int interruptHandlerSegment)
//    {
//        this.machine = machine;
//        this.memory = machine.GetMemory();
//        this.callbackHandlerSegment = interruptHandlerSegment;
//    }

//    public virtual void AddCallback(ICallback callback)
//    {
//        AddService(callback.GetIndex(), callback);
//    }

//    public virtual Dictionary<int, SegmentedAddress> GetCallbackAddresses()
//    {
//        return callbackAddresses;
//    }

//    protected override UnhandledOperationException GenerateUnhandledOperationException(int index)
//    {
//        return new UnhandledCallbackException(machine, index);
//    }

//    public virtual void InstallAllCallbacksInInterruptTable()
//    {
//        base._dispatchTable.Values().Stream().Sorted(Comparator.Comparing(Callback.GetIndex())).ForEach(this.InstallCallbackInInterruptTable());
//    }

//    private void InstallCallbackInInterruptTable(ICallback callback)
//    {
//        offset += InstallInterruptWithCallback(callback.GetIndex(), callbackHandlerSegment, offset);
//    }

//    private int InstallInterruptWithCallback(int vectorNumber, int segment, int offset)
//    {
//        InstallVectorInTable(vectorNumber, segment, offset);
//        return WriteInterruptCallback(vectorNumber, segment, offset);
//    }

//    private int WriteInterruptCallback(int vectorNumber, int segment, int offset)
//    {
//        callbackAddresses.Put(vectorNumber, new SegmentedAddress(segment, offset));
//        int address = MemoryUtils.ToPhysicalAddress(segment, offset);

//        // CALLBACK opcode (custom instruction, FE38 + 16 bits callback number)
//        memory.SetUint8(address, 0xFE);
//        memory.SetUint8(address + 1, 0x38);

//        // vector to call
//        memory.SetUint16(address + 2, vectorNumber);

//        // IRET
//        memory.SetUint8(address + 4, 0xCF);

//        // 5 bytes used
//        return 5;
//    }

//    private void InstallVectorInTable(int vectorNumber, int segment, int offset)
//    {

//        // install the vector in the vector table
//        memory.SetUint16(4 * vectorNumber + 2, segment);
//        memory.SetUint16(4 * vectorNumber, offset);
//    }
//}
