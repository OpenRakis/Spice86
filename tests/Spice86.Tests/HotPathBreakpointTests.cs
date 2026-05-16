namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Utils;

using Xunit;

public class HotPathBreakpointTests {
    [Fact]
    public void ExecutionBreakpoint_OnInteriorInstruction_TriggersAndPausesAtCorrectAddress() {
        // "mov BX,AX" at F000:002D is an interior instruction in the externalint loop body.
        // Loop: inc AX (002C) / mov BX,AX (002D) / shr BX,1 (002F) / loop (0031)
        const ushort segment = 0xF000;
        const ushort movBxAxOffset = 0x002D;
        uint expectedPhysicalAddress = MemoryUtils.ToPhysicalAddress(segment, movBxAxOffset);

        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            "externalint", maxCycles: 0xFFFFFFF, enablePit: true).Create();

        Machine machine = spice86DependencyInjection.Machine;
        State state = machine.CpuState;
        EmulatorBreakpointsManager breakpointsManager = machine.EmulatorBreakpointsManager;
        ProgramExecutor programExecutor = spice86DependencyInjection.ProgramExecutor;
        IPauseHandler pauseHandler = machine.PauseHandler;

        bool breakpointTriggered = false;
        uint capturedIpPhysicalAddress = 0;
        long capturedCycles = -1;
        ushort capturedAx = 0;
        ushort capturedBx = 0;

        AddressBreakPoint breakpoint = new(
            BreakPointType.CPU_EXECUTION_ADDRESS,
            expectedPhysicalAddress,
            bp => {
                breakpointTriggered = true;
                capturedIpPhysicalAddress = state.IpPhysicalAddress;
                capturedCycles = state.Cycles;
                capturedAx = state.AX;
                capturedBx = state.BX;
                pauseHandler.RequestPause("Execution breakpoint hit");
                pauseHandler.Resume();
            },
            isRemovedOnTrigger: true);

        breakpointsManager.ToggleBreakPoint(breakpoint, true);

        programExecutor.Run();

        breakpointTriggered.Should().BeTrue(
            "the execution breakpoint on an interior instruction should fire during hot-path execution");
        capturedIpPhysicalAddress.Should().Be(expectedPhysicalAddress,
            "State.IpPhysicalAddress should match the breakpointed instruction address");

        // inc AX has executed (AX incremented from 0 to 1 on first iteration),
        // but mov BX,AX has NOT yet executed, so BX should still be 0.
        capturedAx.Should().Be(1,
            "AX should reflect the inc AX that ran before the breakpointed instruction");
        capturedBx.Should().Be(0,
            "BX should not yet reflect mov BX,AX since that instruction has not executed");

        // Cycles should reflect all instructions executed before the breakpointed one.
        capturedCycles.Should().BeGreaterThan(0,
            "cycles should reflect execution of instructions prior to the breakpointed one");
    }

    [Fact]
    public void ResumeAfterBreakpoint_ContinuesExecutionFromBreakpointedInstruction() {
        // Breakpoint on "mov bx,ax" at F000:002D
        const uint movBxAxAddress = 0xF002D;
        // Next instruction "shr bx,1" at F000:002F
        const uint shrBx1Address = 0xF002F;

        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            "externalint", maxCycles: 0xFFFFFFF, enablePit: true).Create();

        Machine machine = spice86DependencyInjection.Machine;
        State state = machine.CpuState;
        EmulatorBreakpointsManager breakpointsManager = machine.EmulatorBreakpointsManager;
        ProgramExecutor programExecutor = spice86DependencyInjection.ProgramExecutor;
        IPauseHandler pauseHandler = machine.PauseHandler;

        ushort capturedAxAtMovBxAx = 0;
        ushort capturedBxAtShrBx1 = 0;
        bool movBxAxHit = false;
        bool shrBx1Hit = false;

        AddressBreakPoint movBxAxBreakpoint = new(
            BreakPointType.CPU_EXECUTION_ADDRESS,
            movBxAxAddress,
            bp => {
                movBxAxHit = true;
                capturedAxAtMovBxAx = state.AX;

                // Set a breakpoint on the next instruction to verify mov bx,ax executed
                AddressBreakPoint shrBreakpoint = new(
                    BreakPointType.CPU_EXECUTION_ADDRESS,
                    shrBx1Address,
                    bp2 => {
                        shrBx1Hit = true;
                        capturedBxAtShrBx1 = state.BX;
                        pauseHandler.RequestPause("shr bx,1 breakpoint hit");
                        pauseHandler.Resume();
                    },
                    isRemovedOnTrigger: true);
                breakpointsManager.ToggleBreakPoint(shrBreakpoint, true);

                pauseHandler.RequestPause("mov bx,ax breakpoint hit");
                pauseHandler.Resume();
            },
            isRemovedOnTrigger: true);

        breakpointsManager.ToggleBreakPoint(movBxAxBreakpoint, true);

        programExecutor.Run();

        movBxAxHit.Should().BeTrue("breakpoint on mov bx,ax should fire");
        shrBx1Hit.Should().BeTrue("breakpoint on shr bx,1 should fire after resume");
        capturedBxAtShrBx1.Should().Be(capturedAxAtMovBxAx,
            "after resume, mov bx,ax should have executed, making BX equal to AX's value at that point");
    }

    [Fact]
    public void BreakpointOnFirstInstructionOfBlock_TriggersCorrectly() {
        // "inc ax" at F000:002C is the first instruction of the loop block
        const uint incAxAddress = 0xF002C;

        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            "externalint", maxCycles: 0xFFFFFFF, enablePit: true).Create();

        Machine machine = spice86DependencyInjection.Machine;
        State state = machine.CpuState;
        EmulatorBreakpointsManager breakpointsManager = machine.EmulatorBreakpointsManager;
        ProgramExecutor programExecutor = spice86DependencyInjection.ProgramExecutor;
        IPauseHandler pauseHandler = machine.PauseHandler;

        bool breakpointFired = false;
        uint capturedAddress = 0;

        AddressBreakPoint breakpoint = new(
            BreakPointType.CPU_EXECUTION_ADDRESS,
            incAxAddress,
            bp => {
                breakpointFired = true;
                capturedAddress = state.IpPhysicalAddress;
                pauseHandler.RequestPause("inc ax breakpoint hit");
                pauseHandler.Resume();
            },
            isRemovedOnTrigger: true);

        breakpointsManager.ToggleBreakPoint(breakpoint, true);

        programExecutor.Run();

        breakpointFired.Should().BeTrue("breakpoint on the first instruction of the loop block should fire");
        capturedAddress.Should().Be(incAxAddress,
            "IpPhysicalAddress should match the first instruction of the loop block");
    }

    [Fact]
    public void CycleBreakpoint_DuringHotPathExecution_TriggersAtExpectedCycle() {
        const long targetCycle = 100;
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            "externalint", maxCycles: 0xFFFFFFF, enablePit: true).Create();

        Machine machine = spice86DependencyInjection.Machine;
        State state = machine.CpuState;
        EmulatorBreakpointsManager breakpointsManager = machine.EmulatorBreakpointsManager;
        ProgramExecutor programExecutor = spice86DependencyInjection.ProgramExecutor;
        IPauseHandler pauseHandler = machine.PauseHandler;

        bool breakpointTriggered = false;
        long capturedCycles = -1;

        AddressBreakPoint breakpoint = new(
            BreakPointType.CPU_CYCLES,
            targetCycle,
            bp => {
                breakpointTriggered = true;
                capturedCycles = state.Cycles;
                pauseHandler.RequestPause("Cycle breakpoint hit");
                pauseHandler.Resume();
            },
            isRemovedOnTrigger: true);

        breakpointsManager.ToggleBreakPoint(breakpoint, true);

        programExecutor.Run();

        breakpointTriggered.Should().BeTrue("the cycle breakpoint should fire during hot-path execution");
        capturedCycles.Should().Be(targetCycle, "the cycle count should match the breakpoint target");
    }
}
