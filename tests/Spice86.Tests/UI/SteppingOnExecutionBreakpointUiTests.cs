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
        int machineStartTriggerCount = 0;

        //Act
        //Assert
        RunSteppingScenario(
            new SteppingScenario {
                BinName = "jump1",
                InstallInterruptVectors = false,
                InitialAddress = new SegmentedAddress(0xF000, 0x0000),
                ExpectedAddress = new SegmentedAddress(0xF000, 0x0003),
                RemoveBreakpointOnTrigger = true,
                UseStepInto = true,
                AssertSingleInstructionCycleDelta = false,
                AssertNoTemporaryUiBreakpoints = false,
                PauseBeforeSteppingFailureMessage = "The emulator should pause on the execution breakpoint before stepping",
                InitialAddressAssertionMessage = "the first pause should happen on the instruction under test",
                StepAvailabilityAssertionMessage = "step into should be available while paused",
                DestinationFailureMessage = "the first step should land on the next instruction without requiring a second step",
                CycleAssertionMessage = string.Empty,
                NoTemporaryUiBreakpointsAssertionMessage = string.Empty
            },
            context => {
                UnconditionalBreakPoint machineStartBreakpoint = new(
                    BreakPointType.MACHINE_START,
                    _ => {
                        machineStartTriggerCount++;
                        context.PauseHandler.RequestPause("Machine start breakpoint was reached");
                    },
                    removeOnTrigger: false);

                context.EmulatorBreakpointsManager.ToggleBreakPoint(machineStartBreakpoint, on: true);
                context.EmulatorBreakpointsManager.ToggleBreakPoint(machineStartBreakpoint, on: false);
            },
            _ => {
                machineStartTriggerCount.Should().Be(0,
                    "disabling MACHINE_START should clear the slot so the first step is not consumed by a stale machine-start pause");
            });
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
