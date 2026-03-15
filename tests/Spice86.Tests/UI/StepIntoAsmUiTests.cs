namespace Spice86.Tests.UI;

using Avalonia.Headless.XUnit;

using FluentAssertions;

using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;

public class StepIntoAsmUiTests : BreakpointUiTestBase {
    [AvaloniaFact]
    public void StepInto_OnMov_AdvancesToImmediateNextInstruction() {
        //Arrange
        SegmentedAddress initialAddress = new(0xF000, 0x0000);
        SegmentedAddress expectedAddress = new(0xF000, 0x0003);

        //Act
        //Assert
        RunStepIntoCase("jump1", initialAddress, expectedAddress, installInterruptVectors: false);
    }

    [AvaloniaFact]
    public void StepInto_OnStc_AdvancesToImmediateNextInstruction() {
        //Arrange
        SegmentedAddress initialAddress = new(0xF000, 0x0010);
        SegmentedAddress expectedAddress = new(0xF000, 0x0011);

        //Act
        //Assert
        RunStepIntoCase("jump1", initialAddress, expectedAddress, installInterruptVectors: false);
    }

    [AvaloniaFact]
    public void StepInto_OnCmp_AdvancesToImmediateNextInstruction() {
        //Arrange
        SegmentedAddress initialAddress = new(0xF000, 0x0009);
        SegmentedAddress expectedAddress = new(0xF000, 0x000B);

        //Act
        //Assert
        RunStepIntoCase("cmpneg", initialAddress, expectedAddress, installInterruptVectors: false);
    }

    [AvaloniaFact]
    public void StepInto_OnCall_EntersCallTarget() {
        //Arrange
        SegmentedAddress initialAddress = new(0xF000, 0x000E);
        SegmentedAddress expectedAddress = new(0xF000, 0x1290);

        //Act
        //Assert
        RunStepIntoCase("jump2", initialAddress, expectedAddress, installInterruptVectors: false);
    }

    [AvaloniaFact]
    public void StepInto_OnNearRet_ReturnsToCaller() {
        //Arrange
        SegmentedAddress initialAddress = new(0xF000, 0x12AA);
        SegmentedAddress expectedAddress = new(0xF000, 0x0010);

        //Act
        //Assert
        RunStepIntoCase("jump2", initialAddress, expectedAddress, installInterruptVectors: false);
    }

    [AvaloniaFact]
    public void StepInto_OnUnconditionalJump_GoesToJumpTarget() {
        //Arrange
        SegmentedAddress initialAddress = new(0xF000, 0x000C);
        SegmentedAddress expectedAddress = new(0xF000, 0x0010);

        //Act
        //Assert
        RunStepIntoCase("jump1", initialAddress, expectedAddress, installInterruptVectors: false);
    }

    [AvaloniaFact]
    public void StepInto_OnConditionalJumpNotTaken_GoesToFallThroughWithoutTemporaryUiBreakpoint() {
        //Arrange
        SegmentedAddress initialAddress = new(0xF000, 0x0011);
        SegmentedAddress expectedAddress = new(0xF000, 0x0013);

        //Act
        //Assert
        RunStepIntoCase("jump1", initialAddress, expectedAddress, installInterruptVectors: false);
    }

    [AvaloniaFact]
    public void StepInto_OnConditionalJumpTaken_GoesToTargetWithoutTemporaryUiBreakpoint() {
        //Arrange
        SegmentedAddress initialAddress = new(0xF000, 0x001C);
        SegmentedAddress expectedAddress = new(0xF000, 0x0020);

        //Act
        //Assert
        RunStepIntoCase("jump1", initialAddress, expectedAddress, installInterruptVectors: false);
    }

    [AvaloniaFact]
    public void StepInto_OnInterrupt_EntersInterruptHandler() {
        //Arrange
        SegmentedAddress initialAddress = new(0xF000, 0x0020);
        SegmentedAddress expectedAddress = new(0xE342, 0xEBE0);

        //Act
        //Assert
        RunStepIntoCase("interrupt", initialAddress, expectedAddress, installInterruptVectors: true);
    }

    private void RunStepIntoCase(string binName, SegmentedAddress initialAddress, SegmentedAddress expectedAddress, bool installInterruptVectors) {
        //Arrange
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
            //Act
            WaitUntil(
                () => context.PauseHandler.IsPaused,
                timeoutMilliseconds: 5000,
                failureMessage: "The emulator should pause on the initial breakpoint before stepping");

            WaitUntil(
                () => context.DisassemblyViewModel.IsPaused,
                timeoutMilliseconds: 5000,
                failureMessage: "The disassembly view model should reflect the paused state before stepping");

            context.State.IpSegmentedAddress.Should().Be(initialAddress,
                "the initial breakpoint must pause exactly on the instruction under test");

            context.DisassemblyViewModel.StepIntoCommand.CanExecute(null).Should().BeTrue(
                "step into should be available while paused");

            long initialCycles = context.State.Cycles;
            context.DisassemblyViewModel.StepIntoCommand.Execute(null);

            WaitUntil(
                () => context.PauseHandler.IsPaused && context.State.IpSegmentedAddress == expectedAddress,
                timeoutMilliseconds: 5000,
                failureMessage: "step into should execute exactly one instruction and pause at the expected destination");

            WaitUntil(
                () => context.DisassemblyViewModel.IsPaused,
                timeoutMilliseconds: 5000,
                failureMessage: "The disassembly view model should be paused after step into completes");

            //Assert
            context.State.Cycles.Should().Be(initialCycles + 1,
                "step into should execute exactly one instruction and must not skip ASM lines");

            context.BreakpointsViewModel.Breakpoints.Should().BeEmpty(
                "step into must not create or leave temporary UI breakpoints");
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
