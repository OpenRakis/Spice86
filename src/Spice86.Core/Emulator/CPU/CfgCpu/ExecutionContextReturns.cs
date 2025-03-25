namespace Spice86.Core.Emulator.CPU.CfgCpu;

using Spice86.Shared.Emulator.Memory;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;

/// <summary>
/// Maps return address to a stack of execution contexts to restore.
/// We create a new context each time an int occurs and register where we consider this context to end here.
/// If a program doesn't disable interrupts while in the interrupt handler, the same interrupt could happen again and again without leaving the handler.
/// Those stacked handlers can very well by chance happen at the same address and so return at the same address.
/// To handle gracefully that case and no restore any arbitrary context, we map the expected return address to a stack of contexts so that they are unstacked in the correct order when they complete.
/// </summary>
public class ExecutionContextReturns {
    private readonly Dictionary<SegmentedAddress, Stack<ExecutionContext>> _executionContextReturns = new();

    public void PushContextToRestore(SegmentedAddress expectedReturn, ExecutionContext contextToRestore) {
        // Save current execution context
        if (!_executionContextReturns.TryGetValue(expectedReturn, out Stack<ExecutionContext>? contextsToRestoreAtAddress)) {
            contextsToRestoreAtAddress = new Stack<ExecutionContext>();
            _executionContextReturns[expectedReturn] = contextsToRestoreAtAddress;
        }
        contextsToRestoreAtAddress.Push(contextToRestore);
    }

    public ExecutionContext? TryRestoreContext(SegmentedAddress returnAddress) {
        if (!_executionContextReturns.TryGetValue(returnAddress, out Stack<ExecutionContext>? contextsToRestoreAtAddress)) {
            return null;
        }
        ExecutionContext? res = contextsToRestoreAtAddress.Pop();
        if (contextsToRestoreAtAddress.Count == 0) {
            _executionContextReturns.Remove(returnAddress);
        }
        return res;
    }
}