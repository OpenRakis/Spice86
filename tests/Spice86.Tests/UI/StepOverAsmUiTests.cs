namespace Spice86.Tests.UI;

using Avalonia.Headless.XUnit;

using FluentAssertions;

using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;

public class StepOverAsmUiTests : BreakpointUiTestBase {
    [AvaloniaFact]
    public void StepOver_OnCall_PausesAtNextInstruction() {
        SegmentedAddress initialAddress = new(0xF000, 0x000E);
        SegmentedAddress expectedAddress = new(0xF000, 0x0010);
        RunStepOverCase("jump2", initialAddress, expectedAddress, installInterruptVectors: false, assertSingleInstructionCycleDelta: false);
    }

    [AvaloniaFact]
    public void StepOver_OnMov_AdvancesToImmediateNextInstruction() {
        SegmentedAddress initialAddress = new(0xF000, 0x0000);
        SegmentedAddress expectedAddress = new(0xF000, 0x0003);
        RunStepOverCase("jump1", initialAddress, expectedAddress, installInterruptVectors: false, assertSingleInstructionCycleDelta: true);
    }

    [AvaloniaFact]
    public void StepOver_OnStc_AdvancesToImmediateNextInstruction() {
        SegmentedAddress initialAddress = new(0xF000, 0x0010);
        SegmentedAddress expectedAddress = new(0xF000, 0x0011);
        RunStepOverCase("jump1", initialAddress, expectedAddress, installInterruptVectors: false, assertSingleInstructionCycleDelta: true);
    }

    private void RunStepOverCase(string binName, SegmentedAddress initialAddress, SegmentedAddress expectedAddress,
        bool installInterruptVectors, bool assertSingleInstructionCycleDelta) {
        // Arrange
        using Spice86DependencyInjection dependencyInjection = new Spice86Creator(
            binName,
            enablePit: false,
            maxCycles: 2_000_000,
            installInterruptVectors: installInterruptVectors).Create();
        DisassemblySteppingContext context = CreateActiveDisassemblySteppingContext(dependencyInjection);

        AddressBreakPoint initialPauseBreakpoint =
            CreateExecutionPauseBreakpoint(initialAddress, context.PauseHandler, removeOnTrigger: true);
        context.EmulatorBreakpointsManager.ToggleBreakPoint(initialPauseBreakpoint, on: true);

        Task runTask = Task.Run(() => dependencyInjection.ProgramExecutor.Run());

        try {
            // Act
            WaitUntil(
                () => context.PauseHandler.IsPaused && context.DisassemblyViewModel.IsPaused,
                timeoutMilliseconds: 5000,
                failureMessage: "The emulator should pause on the initial breakpoint before stepping");

            context.State.IpSegmentedAddress.Should().Be(initialAddress,
                "the initial breakpoint must pause exactly on the instruction under test");

            context.DisassemblyViewModel.StepOverCommand.CanExecute(null).Should().BeTrue(
                "step over should be available while paused");

            long initialCycles = context.State.Cycles;
            context.DisassemblyViewModel.StepOverCommand.Execute(null);

            // Assert
            WaitUntil(
                () => context.PauseHandler.IsPaused
                      && context.DisassemblyViewModel.IsPaused
                      && context.State.IpSegmentedAddress == expectedAddress,
                timeoutMilliseconds: 5000,
                failureMessage: "step over should pause at the expected destination");

            if (assertSingleInstructionCycleDelta) {
                context.State.Cycles.Should().Be(initialCycles + 1,
                    "step over should execute exactly one instruction for non-call instructions");
            }

            context.BreakpointsViewModel.Breakpoints.Should().BeEmpty(
                "step over must not create or leave temporary UI breakpoints");
        } finally {
            context.State.IsRunning = false;
            context.PauseHandler.Resume();
            WaitUntil(
                () => runTask.IsCompleted,
                timeoutMilliseconds: 5000,
                failureMessage: "The emulation task should complete during cleanup");

            runTask.IsFaulted.Should().BeFalse("cleanup should not leave a faulted background run task");

            ProcessUiEvents();
        }
    }
}
