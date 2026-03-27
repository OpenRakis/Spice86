namespace Spice86.Core.Emulator.Debugger;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;

using System;

/// <summary>Sets up step-breakpoints. Logic shared between the UI debugger and MCP tools.</summary>
public static class DebuggerStepHelper
{
    /// <summary>
    /// Registers a one-shot CPU_CYCLES breakpoint that fires after exactly one instruction executes.
    /// </summary>
    /// <param name="breakpointsManager">The breakpoints manager to register with.</param>
    /// <param name="state">Current CPU state, used to compute the trigger cycle.</param>
    /// <param name="onHit">Callback invoked on the emulation thread when the breakpoint fires.</param>
    public static void SetupStepIntoBreakpoint(EmulatorBreakpointsManager breakpointsManager, State state, Action onHit)
    {
        long triggerCycle = state.Cycles + 1;
        AddressBreakPoint breakpoint = new(BreakPointType.CPU_CYCLES, triggerCycle, _ => onHit(), true);
        breakpointsManager.ToggleBreakPoint(breakpoint, on: true);
    }

    /// <summary>
    /// Registers a one-shot execution-address breakpoint for step-over of a CALL or INT instruction.
    /// Pauses when execution reaches <paramref name="nextAddress"/> after the called routine returns.
    /// </summary>
    /// <param name="breakpointsManager">The breakpoints manager to register with.</param>
    /// <param name="nextAddress">Linear address of the instruction immediately following the current one.</param>
    /// <param name="onHit">Callback invoked on the emulation thread when the breakpoint fires.</param>
    public static void SetupStepOverBreakpoint(EmulatorBreakpointsManager breakpointsManager, uint nextAddress, Action onHit)
    {
        AddressBreakPoint breakpoint = new(BreakPointType.CPU_EXECUTION_ADDRESS, nextAddress, _ => onHit(), true);
        breakpointsManager.ToggleBreakPoint(breakpoint, on: true);
    }
}
