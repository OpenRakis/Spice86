namespace Spice86.Core.Emulator.CPU.CfgCpu.CallFlow;

using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;

public class CallStackViewer {
    private readonly State _state;
    private readonly IMemory _memory;

    public CallStackViewer(State state, IMemory memory) {
        _state = state;
        _memory = memory;
    }
    /// <summary>
    /// Returns the return address of the specified call type from the machine stack without removing it.
    /// </summary>
    /// <param name="returnCallType">The type of the return call.</param>
    /// <returns>The return address of the specified call type from the machine stack without removing it.</returns>
    public SegmentedAddress PeekReturnAddressOnMachineStack(CallType returnCallType) {
        return PeekReturnAddressOnMachineStack(returnCallType, _state.StackPhysicalAddress);
    }

    /// <summary>
    /// Returns the return address of the specified call type from the machine stack at the specified physical address without removing it.
    /// </summary>
    /// <param name="returnCallType">The calling convention.</param>
    /// <param name="stackPhysicalAddress">The physical address of the stack.</param>
    /// <returns>The return address of the specified call type from the machine stack at the specified physical address without removing it.</returns>
    public SegmentedAddress PeekReturnAddressOnMachineStack(CallType returnCallType, uint stackPhysicalAddress) {
        IMemory memory = _memory;
        return returnCallType switch {
            CallType.NEAR => new SegmentedAddress(_state.CS, memory.UInt16[stackPhysicalAddress]),
            CallType.FAR or CallType.INTERRUPT => memory.SegmentedAddress[stackPhysicalAddress],
            _ => throw new ArgumentException($"Call type {returnCallType} is not supported.", nameof(returnCallType))
        };
    }
}