namespace Spice86.Tests.UI;

using Avalonia.Headless.XUnit;

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
        RunSteppingScenario(new SteppingScenario {
            BinName = binName,
            InstallInterruptVectors = installInterruptVectors,
            InitialAddress = initialAddress,
            ExpectedAddress = expectedAddress,
            RemoveBreakpointOnTrigger = true,
            UseStepInto = false,
            AssertSingleInstructionCycleDelta = assertSingleInstructionCycleDelta,
            AssertNoTemporaryUiBreakpoints = true,
            PauseBeforeSteppingFailureMessage = "The emulator should pause on the initial breakpoint before stepping",
            InitialAddressAssertionMessage = "the initial breakpoint must pause exactly on the instruction under test",
            StepAvailabilityAssertionMessage = "step over should be available while paused",
            DestinationFailureMessage = "step over should pause at the expected destination",
            CycleAssertionMessage = "step over should execute exactly one instruction for non-call instructions",
            NoTemporaryUiBreakpointsAssertionMessage = "step over must not create or leave temporary UI breakpoints"
        });
    }
}
