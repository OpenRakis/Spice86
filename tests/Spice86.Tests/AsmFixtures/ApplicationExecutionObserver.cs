namespace Spice86.Tests.AsmFixtures;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Utils;

/// <summary>
/// Records every node the interpreter was about to execute during one override run, together with the
/// node kind and the instruction bytes at that address. The breakpoint is installed just before the
/// program starts and removed again as soon as the run ends, so the observation has the lifetime of
/// exactly one run and never retains an earlier machine.
/// </summary>
internal sealed class ApplicationExecutionObserver : IDisposable {
    /// <summary>Longest x86 instruction, the most bytes worth recording at one address.</summary>
    private const int MaxInstructionBytes = 15;

    private readonly List<ObservedExecution> _executions = [];
    private Machine? _machine;
    private UnconditionalBreakPoint? _breakPoint;

    /// <summary>Nodes in execution order, one entry per interpreter-executed node.</summary>
    public IReadOnlyList<ObservedExecution> Executions => _executions;

    /// <summary>Addresses in execution order, one entry per interpreter-executed node.</summary>
    public IReadOnlyList<SegmentedAddress> ExecutedAddresses =>
        [.. _executions.Select(execution => execution.Address)];

    /// <summary>Whether the observer is still subscribed to a machine.</summary>
    public bool IsInstalled => _breakPoint != null;

    /// <summary>Starts observing the interpreter execution of <paramref name="machine"/>.</summary>
    /// <param name="machine">Machine whose interpreter execution is observed.</param>
    public void Install(Machine machine) {
        _machine = machine;
        _breakPoint = new UnconditionalBreakPoint(BreakPointType.CPU_EXECUTION_ADDRESS, OnNodeAboutToExecute, false);
        machine.EmulatorBreakpointsManager.ToggleBreakPoint(_breakPoint, true);
    }

    /// <summary>Removes the breakpoint this observer installed, if it still holds one.</summary>
    public void Uninstall() {
        Machine? machine = _machine;
        UnconditionalBreakPoint? breakPoint = _breakPoint;
        _machine = null;
        _breakPoint = null;
        if (machine == null || breakPoint == null) {
            return;
        }
        machine.EmulatorBreakpointsManager.ToggleBreakPoint(breakPoint, false);
    }

    /// <inheritdoc />
    public void Dispose() {
        Uninstall();
    }

    private void OnNodeAboutToExecute(BreakPoint breakPoint) {
        Machine? machine = _machine;
        if (machine == null) {
            return;
        }
        // CfgCpu assigns the node it is about to execute before checking execution breakpoints, so the
        // node's own address is the exact instruction that would run next.
        ICfgNode? node = machine.CfgCpu.ExecutionContextManager.ExecutingNode;
        if (node == null) {
            return;
        }
        byte[] instructionBytes = ReadInstructionBytes(machine.Memory, node.Address, BytesToRecord(node));
        _executions.Add(new ObservedExecution(node.Address, node.GetType().Name, instructionBytes));
    }

    /// <summary>How many bytes of the instruction at that node are worth recording: a whole instruction
    /// when the node is one, and a single byte for the dispatch nodes that are not instructions.</summary>
    /// <param name="node">Node the interpreter was about to execute.</param>
    /// <returns>Number of bytes to record.</returns>
    private static int BytesToRecord(ICfgNode node) {
        if (node is not CfgInstruction instruction) {
            return 1;
        }
        return Math.Clamp((int)instruction.Length, 1, MaxInstructionBytes);
    }

    private static byte[] ReadInstructionBytes(IMemory memory, SegmentedAddress address, int length) {
        byte[] bytes = new byte[length];
        for (int i = 0; i < length; i++) {
            bytes[i] = memory.SneakilyRead(MemoryUtils.ToPhysicalAddress(address.Segment, (ushort)(address.Offset + i)));
        }
        return bytes;
    }
}
