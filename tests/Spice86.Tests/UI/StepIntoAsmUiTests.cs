namespace Spice86.Tests.UI;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.Messaging;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;
using Spice86.ViewModels;
using Spice86.ViewModels.Services;

using System.Diagnostics;

public class StepIntoAsmUiTests : BreakpointUiTestBase {
    [AvaloniaFact]
    public void StepInto_OnMov_AdvancesToImmediateNextInstruction() {
        SegmentedAddress initialAddress = new(0xF000, 0x0000);
        SegmentedAddress expectedAddress = new(0xF000, 0x0003);
        RunStepIntoCase("jump1", initialAddress, expectedAddress, installInterruptVectors: false);
    }

    [AvaloniaFact]
    public void StepInto_OnStc_AdvancesToImmediateNextInstruction() {
        SegmentedAddress initialAddress = new(0xF000, 0x0010);
        SegmentedAddress expectedAddress = new(0xF000, 0x0011);
        RunStepIntoCase("jump1", initialAddress, expectedAddress, installInterruptVectors: false);
    }

    [AvaloniaFact]
    public void StepInto_OnCmp_AdvancesToImmediateNextInstruction() {
        SegmentedAddress initialAddress = new(0xF000, 0x0009);
        SegmentedAddress expectedAddress = new(0xF000, 0x000B);
        RunStepIntoCase("cmpneg", initialAddress, expectedAddress, installInterruptVectors: false);
    }

    [AvaloniaFact]
    public void StepInto_OnCall_EntersCallTarget() {
        SegmentedAddress initialAddress = new(0xF000, 0x000E);
        SegmentedAddress expectedAddress = new(0xF000, 0x1290);
        RunStepIntoCase("jump2", initialAddress, expectedAddress, installInterruptVectors: false);
    }

    [AvaloniaFact]
    public void StepInto_OnNearRet_ReturnsToCaller() {
        SegmentedAddress initialAddress = new(0xF000, 0x12AA);
        SegmentedAddress expectedAddress = new(0xF000, 0x0010);
        RunStepIntoCase("jump2", initialAddress, expectedAddress, installInterruptVectors: false);
    }

    [AvaloniaFact]
    public void StepInto_OnUnconditionalJump_GoesToJumpTarget() {
        SegmentedAddress initialAddress = new(0xF000, 0x000C);
        SegmentedAddress expectedAddress = new(0xF000, 0x0010);
        RunStepIntoCase("jump1", initialAddress, expectedAddress, installInterruptVectors: false);
    }

    [AvaloniaFact]
    public void StepInto_OnConditionalJumpNotTaken_GoesToFallThroughWithoutTemporaryUiBreakpoint() {
        SegmentedAddress initialAddress = new(0xF000, 0x0011);
        SegmentedAddress expectedAddress = new(0xF000, 0x0013);
        RunStepIntoCase("jump1", initialAddress, expectedAddress, installInterruptVectors: false);
    }

    [AvaloniaFact]
    public void StepInto_OnConditionalJumpTaken_GoesToTargetWithoutTemporaryUiBreakpoint() {
        SegmentedAddress initialAddress = new(0xF000, 0x001C);
        SegmentedAddress expectedAddress = new(0xF000, 0x0020);
        RunStepIntoCase("jump1", initialAddress, expectedAddress, installInterruptVectors: false);
    }

    [AvaloniaFact]
    public void StepInto_OnInterrupt_EntersInterruptHandler() {
        SegmentedAddress initialAddress = new(0xF000, 0x0020);
        SegmentedAddress expectedAddress = new(0xE342, 0xEBE0);
        RunStepIntoCase("interrupt", initialAddress, expectedAddress, installInterruptVectors: true);
    }

    private void RunStepIntoCase(string binName, SegmentedAddress initialAddress, SegmentedAddress expectedAddress, bool installInterruptVectors) {
        using Spice86DependencyInjection dependencyInjection = new Spice86Creator(
            binName,
            enablePit: false,
            maxCycles: 2_000_000,
            installInterruptVectors: installInterruptVectors).Create();

        State state = dependencyInjection.Machine.CpuState;
        IPauseHandler pauseHandler = dependencyInjection.Machine.PauseHandler;
        EmulatorBreakpointsManager emulatorBreakpointsManager = dependencyInjection.Machine.EmulatorBreakpointsManager;
        IMemory memory = dependencyInjection.Machine.Memory;

        ILoggerService loggerService = CreateMockLoggerService();
        IUIDispatcher uiDispatcher = CreateUIDispatcher();
        IMessenger messenger = CreateMessenger();
        ITextClipboard textClipboard = Substitute.For<ITextClipboard>();

        BreakpointsViewModel breakpointsViewModel = new(
            state,
            pauseHandler,
            messenger,
            emulatorBreakpointsManager,
            uiDispatcher,
            textClipboard,
            memory);

        IDictionary<SegmentedAddress, FunctionInformation> functionsInformation =
            new Dictionary<SegmentedAddress, FunctionInformation>();

        DisassemblyViewModel disassemblyViewModel = new(
            emulatorBreakpointsManager,
            memory,
            state,
            functionsInformation,
            breakpointsViewModel,
            pauseHandler,
            uiDispatcher,
            messenger,
            textClipboard,
            loggerService);

        disassemblyViewModel.Activate();

        long initialLinearAddress = MemoryUtils.ToPhysicalAddress(initialAddress.Segment, initialAddress.Offset);
        AddressBreakPoint initialPauseBreakpoint = new(
            BreakPointType.CPU_EXECUTION_ADDRESS,
            initialLinearAddress,
            _ => {
                pauseHandler.RequestPause($"Initial pause reached at {initialAddress}");
                pauseHandler.WaitIfPaused();
            },
            true);

        emulatorBreakpointsManager.ToggleBreakPoint(initialPauseBreakpoint, on: true);

        Task runTask = Task.Run(() => dependencyInjection.ProgramExecutor.Run());

        try {
            WaitUntil(
                () => pauseHandler.IsPaused,
                timeoutMilliseconds: 5000,
                failureMessage: "The emulator should pause on the initial breakpoint before stepping");

            WaitUntil(
                () => disassemblyViewModel.IsPaused,
                timeoutMilliseconds: 5000,
                failureMessage: "The disassembly view model should reflect the paused state before stepping");

            state.IpSegmentedAddress.Should().Be(initialAddress,
                "the initial breakpoint must pause exactly on the instruction under test");

            disassemblyViewModel.StepIntoCommand.CanExecute(null).Should().BeTrue(
                "step into should be available while paused");

            long initialCycles = state.Cycles;
            disassemblyViewModel.StepIntoCommand.Execute(null);

            WaitUntil(
                () => pauseHandler.IsPaused && state.IpSegmentedAddress == expectedAddress,
                timeoutMilliseconds: 5000,
                failureMessage: "step into should execute exactly one instruction and pause at the expected destination");

            WaitUntil(
                () => disassemblyViewModel.IsPaused,
                timeoutMilliseconds: 5000,
                failureMessage: "The disassembly view model should be paused after step into completes");

            state.Cycles.Should().Be(initialCycles + 1,
                "step into should execute exactly one instruction and must not skip ASM lines");

            breakpointsViewModel.Breakpoints.Should().BeEmpty(
                "step into must not create or leave temporary UI breakpoints");
        } finally {
            state.IsRunning = false;
            pauseHandler.Resume();
            WaitUntil(
                () => runTask.IsCompleted,
                timeoutMilliseconds: 5000,
                failureMessage: "The emulation task should complete during cleanup");

            runTask.IsFaulted.Should().BeFalse("cleanup should not leave a faulted background run task");

            ProcessUiEvents();
        }
    }

    internal static void WaitUntil(Func<bool> condition, int timeoutMilliseconds, string failureMessage) {
        Stopwatch stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds) {
            Dispatcher.UIThread.RunJobs();
            if (condition()) {
                return;
            }
            Thread.Sleep(1);
        }

        condition().Should().BeTrue(failureMessage);
    }
}
