namespace Spice86.Tests.AsmFixtures;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;

/// <summary>
/// Records every call instruction a run executes through an execution breakpoint: the
/// <see cref="BreakPointType.CPU_EXECUTION_ADDRESS"/> breakpoint fires before the node's effects
/// run, so the captured CS:IP is the call's own address and SS:SP is the stack pointer the callee
/// starts with - any argument pushes already ran, so the bytes they wrote sit at
/// <c>[SS:SP, SS:SP + argumentBytes)</c>. The differential gate uses those records to exclude
/// exactly the argument bytes a generated <c>CallWithStackSpace</c> site reserved without writing.
/// </summary>
internal sealed class StackCallRecorder : IDisposable {
    private readonly EmulatorBreakpointsManager _breakpoints;
    private readonly UnconditionalBreakPoint _breakPoint;
    private readonly WeakReference<Machine> _machine;
    private readonly State _cpuState;

    public StackCallRecorder(Machine machine) {
        _breakpoints = machine.EmulatorBreakpointsManager;
        // Weak: the recorder lives in the run's observations, which can outlive a disposed run -
        // a strong machine reference would keep the whole graph collectible-unreachable.
        _machine = new WeakReference<Machine>(machine);
        _cpuState = machine.CpuState;
        _breakPoint = new UnconditionalBreakPoint(BreakPointType.CPU_EXECUTION_ADDRESS,
            (_, _) => OnNodeAboutToExecute(), false);
        _breakpoints.ToggleBreakPoint(_breakPoint, true);
    }

    /// <summary>The call instruction address and pre-call stack pointer of every executed call,
    /// in execution order.</summary>
    public List<(SegmentedAddress Address, SegmentedAddress StackPointer)> Calls { get; } = [];

    private void OnNodeAboutToExecute() {
        // CfgCpu assigns the node it is about to execute before checking execution breakpoints,
        // so the node's own address is the exact call that would run next.
        if (_machine.TryGetTarget(out Machine? machine)
                && machine.CfgCpu.ExecutionContextManager.ExecutingNode
                    is CfgInstruction { IsCall: true } instruction) {
            Calls.Add((instruction.Address, new SegmentedAddress(_cpuState.SS, _cpuState.SP)));
        }
    }

    public void Dispose() =>
        _breakpoints.ToggleBreakPoint(_breakPoint, false);
}
