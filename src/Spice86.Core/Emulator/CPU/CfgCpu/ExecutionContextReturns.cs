namespace Spice86.Core.Emulator.CPU.CfgCpu;

using Spice86.Shared.Emulator.Memory;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;

/// <summary>
/// Maps return address to a stack of execution contexts to restore.
/// Stack is because in some cases, code can call a new execution context that will return to the same address as the current one, so we need to know the call order to restore the correct one
/// Cant be done with breakpoints since sometimes external int will happen even before instruction after ret / iret is executed
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