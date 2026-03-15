namespace Spice86.Tests.UI;

using Avalonia.Headless.XUnit;

using FluentAssertions;

using Spice86.Core.Emulator.VM.Breakpoint;

using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;

public class SteppingOnExecutionBreakpointUiTests : BreakpointUiTestBase {
    [AvaloniaFact]
    public void StepInto_WhenMachineStartBreakpointWasDisabled_DoesNotTriggerMachineStartAgain() {
        //Arrange
        SegmentedAddress initialAddress = new(0xF000, 0x0000);
        SegmentedAddress expectedAddress = new(0xF000, 0x0003);

        using Spice86DependencyInjection dependencyInjection = new Spice86Creator(
            "jump1",
            enablePit: false,
            maxCycles: 2_000_000,
            installInterruptVectors: false).Create();

        DisassemblySteppingContext context = CreateActiveDisassemblySteppingContext(dependencyInjection);
        int machineStartTriggerCount = 0;

        UnconditionalBreakPoint machineStartBreakpoint = new(
            BreakPointType.MACHINE_START,
            _ => {
                machineStartTriggerCount++;
                context.PauseHandler.RequestPause("Machine start breakpoint was reached");
            },
            removeOnTrigger: false);

        AddressBreakPoint executionBreakpoint =
            CreateExecutionPauseBreakpoint(initialAddress, context.PauseHandler, removeOnTrigger: true);

        context.EmulatorBreakpointsManager.ToggleBreakPoint(machineStartBreakpoint, on: true);
        context.EmulatorBreakpointsManager.ToggleBreakPoint(machineStartBreakpoint, on: false);
        context.EmulatorBreakpointsManager.ToggleBreakPoint(executionBreakpoint, on: true);

        Task runTask = Task.Run(() => dependencyInjection.ProgramExecutor.Run());

        try {
            //Act
            WaitUntil(
                () => context.PauseHandler.IsPaused && context.DisassemblyViewModel.IsPaused,
                timeoutMilliseconds: 5000,
                failureMessage: "The emulator should pause on the execution breakpoint before stepping");

            context.State.IpSegmentedAddress.Should().Be(initialAddress,
                "the first pause should happen on the instruction under test");

            context.DisassemblyViewModel.StepIntoCommand.CanExecute(null).Should().BeTrue(
                "step into should be available while paused");
            context.DisassemblyViewModel.StepIntoCommand.Execute(null);

            //Assert
            WaitUntil(
                () => context.PauseHandler.IsPaused
                      && context.DisassemblyViewModel.IsPaused
                      && context.State.IpSegmentedAddress == expectedAddress,
                timeoutMilliseconds: 5000,
                failureMessage: "the first step should land on the next instruction without requiring a second step");

            machineStartTriggerCount.Should().Be(0,
                "disabling MACHINE_START should clear the slot so the first step is not consumed by a stale machine-start pause");
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

    [AvaloniaFact]
    public void StepInto_OnPersistentExecutionBreakpoint_AdvancesToNextInstruction() {
        //Arrange
        bool useStepInto = true;

        //Act
        //Assert
        RunStepCommandOnPersistentExecutionBreakpointCase(useStepInto: useStepInto);
    }

    [AvaloniaFact]
    public void StepOver_OnPersistentExecutionBreakpoint_AdvancesToNextInstruction() {
        //Arrange
        bool useStepInto = false;

        //Act
        //Assert
        RunStepCommandOnPersistentExecutionBreakpointCase(useStepInto: useStepInto);
    }

    private void RunStepCommandOnPersistentExecutionBreakpointCase(bool useStepInto) {
        //Arrange
        SegmentedAddress initialAddress = new(0xF000, 0x0000);
        SegmentedAddress expectedAddress = new(0xF000, 0x0003);

        //Act
        //Assert
        RunSteppingScenario(new SteppingScenario {
            BinName = "jump1",
            InstallInterruptVectors = false,
            InitialAddress = initialAddress,
            ExpectedAddress = expectedAddress,
            RemoveBreakpointOnTrigger = false,
            UseStepInto = useStepInto,
            AssertSingleInstructionCycleDelta = true,
            AssertNoTemporaryUiBreakpoints = false,
            PauseBeforeSteppingFailureMessage = "The emulator should pause on the persistent execution breakpoint before stepping",
            InitialAddressAssertionMessage = "the persistent execution breakpoint should pause on the instruction under test",
            StepAvailabilityAssertionMessage = useStepInto
                ? "step into should be available while paused"
                : "step over should be available while paused",
            DestinationFailureMessage = "stepping should advance to the next instruction even with a persistent execution breakpoint at the current address",
            CycleAssertionMessage = "stepping should execute exactly one instruction",
            NoTemporaryUiBreakpointsAssertionMessage = string.Empty
        });
    }
}
