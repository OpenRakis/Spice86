namespace Spice86.Tests.UI;

using Avalonia.Headless.XUnit;

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
        SegmentedAddress initialAddress = new(0xF000, 0x0000);
        SegmentedAddress expectedAddress = new(0xF000, 0x0003);

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
