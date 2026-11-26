namespace Spice86.Tests.AsmFixtures;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Records every software interrupt a run executes through an interrupt breakpoint: the
/// <see cref="Breakpoint.BreakPointType.CPU_INTERRUPT"/> breakpoint fires before IP moves and
/// before the frame push, so the captured CS:IP is the <c>int</c> instruction's own address and
/// SS:SP is the caller stack pointer the pushed frame would have occupied below.
/// </summary>
internal sealed class SoftwareInterruptRecorder : IDisposable {
    private readonly EmulatorBreakpointsManager _breakpoints;
    private readonly UnconditionalBreakPoint _breakPoint;

    public SoftwareInterruptRecorder(Machine machine) {
        _breakpoints = machine.EmulatorBreakpointsManager;
        // Captures the state, not the machine, so a disposed run's machine stays collectible.
        State cpuState = machine.CpuState;
        _breakPoint = new UnconditionalBreakPoint(BreakPointType.CPU_INTERRUPT,
            (_, _) => Interrupts.Add((cpuState.IpSegmentedAddress,
                new SegmentedAddress(cpuState.SS, cpuState.SP))), false);
        _breakpoints.ToggleBreakPoint(_breakPoint, true);
    }

    /// <summary>The <c>int</c> address and pre-push stack pointer of every executed software
    /// interrupt, in execution order.</summary>
    public List<(SegmentedAddress Address, SegmentedAddress StackPointer)> Interrupts { get; } = [];

    public void Dispose() =>
        _breakpoints.ToggleBreakPoint(_breakPoint, false);
}
