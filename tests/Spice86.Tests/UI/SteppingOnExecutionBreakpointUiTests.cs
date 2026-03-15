namespace Spice86.Tests.UI;

using Avalonia.Headless.XUnit;

using FluentAssertions;

using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;

public class SteppingOnExecutionBreakpointUiTests : BreakpointUiTestBase {
    [AvaloniaFact]
    public void StepInto_OnPersistentExecutionBreakpoint_AdvancesToNextInstruction() {
        RunStepCommandOnPersistentExecutionBreakpointCase(useStepInto: true);
    }

    [AvaloniaFact]
    public void StepOver_OnPersistentExecutionBreakpoint_AdvancesToNextInstruction() {
        RunStepCommandOnPersistentExecutionBreakpointCase(useStepInto: false);
    }

    private void RunStepCommandOnPersistentExecutionBreakpointCase(bool useStepInto) {
        // Arrange
        SegmentedAddress initialAddress = new(0xF000, 0x0000);
        SegmentedAddress expectedAddress = new(0xF000, 0x0003);

        using Spice86DependencyInjection dependencyInjection = new Spice86Creator(
            "jump1",
            enablePit: false,
            maxCycles: 2_000_000,
            installInterruptVectors: false).Create();
        DisassemblySteppingContext context = CreateActiveDisassemblySteppingContext(dependencyInjection);

        AddressBreakPoint persistentExecutionBreakpoint =
            CreateExecutionPauseBreakpoint(initialAddress, context.PauseHandler, removeOnTrigger: false);
        context.EmulatorBreakpointsManager.ToggleBreakPoint(persistentExecutionBreakpoint, on: true);

        Task runTask = Task.Run(() => dependencyInjection.ProgramExecutor.Run());

        try {
            // Act
            WaitUntil(
                () => context.PauseHandler.IsPaused && context.DisassemblyViewModel.IsPaused,
                timeoutMilliseconds: 5000,
                failureMessage: "The emulator should pause on the persistent execution breakpoint before stepping");

            context.State.IpSegmentedAddress.Should().Be(initialAddress,
                "the persistent execution breakpoint should pause on the instruction under test");

            long initialCycles = context.State.Cycles;

            if (useStepInto) {
                context.DisassemblyViewModel.StepIntoCommand.CanExecute(null).Should().BeTrue(
                    "step into should be available while paused");
                context.DisassemblyViewModel.StepIntoCommand.Execute(null);
            } else {
                context.DisassemblyViewModel.StepOverCommand.CanExecute(null).Should().BeTrue(
                    "step over should be available while paused");
                context.DisassemblyViewModel.StepOverCommand.Execute(null);
            }

            // Assert
            WaitUntil(
                () => context.PauseHandler.IsPaused
                      && context.DisassemblyViewModel.IsPaused
                      && context.State.IpSegmentedAddress == expectedAddress,
                timeoutMilliseconds: 5000,
                failureMessage: "stepping should advance to the next instruction even with a persistent execution breakpoint at the current address");

            context.State.Cycles.Should().Be(initialCycles + 1,
                "stepping should execute exactly one instruction");
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
