namespace Spice86.Tests.UI;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using System.Diagnostics;

using CommunityToolkit.Mvvm.Messaging;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Logging;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;
using Spice86.ViewModels;
using Spice86.ViewModels.Services;

using Xunit;

public class StepOverAsmUiTests : BreakpointUiTestBase {
    [AvaloniaFact]
    public async Task StepOver_OnCall_PausesAtNextInstruction() {
        SegmentedAddress initialAddress = new(0xF000, 0x000E);
        SegmentedAddress expectedAddress = new(0xF000, 0x0010);
        await RunStepOverCase("jump2", initialAddress, expectedAddress, installInterruptVectors: false, assertSingleInstructionCycleDelta: false);
    }

    [AvaloniaFact]
    public async Task StepOver_OnMov_AdvancesToImmediateNextInstruction() {
        SegmentedAddress initialAddress = new(0xF000, 0x0000);
        SegmentedAddress expectedAddress = new(0xF000, 0x0003);
        await RunStepOverCase("jump1", initialAddress, expectedAddress, installInterruptVectors: false, assertSingleInstructionCycleDelta: true);
    }

    [AvaloniaFact]
    public async Task StepOver_OnStc_AdvancesToImmediateNextInstruction() {
        SegmentedAddress initialAddress = new(0xF000, 0x0010);
        SegmentedAddress expectedAddress = new(0xF000, 0x0011);
        await RunStepOverCase("jump1", initialAddress, expectedAddress, installInterruptVectors: false, assertSingleInstructionCycleDelta: true);
    }

    private async Task RunStepOverCase(string binName, SegmentedAddress initialAddress, SegmentedAddress expectedAddress,
        bool installInterruptVectors, bool assertSingleInstructionCycleDelta) {
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
            StepIntoAsmUiTests.WaitUntil(
                () => pauseHandler.IsPaused && disassemblyViewModel.IsPaused,
                timeoutMilliseconds: 5000,
                failureMessage: "The emulator should pause on the initial breakpoint before stepping");

            state.IpSegmentedAddress.Should().Be(initialAddress,
                "the initial breakpoint must pause exactly on the instruction under test");

            disassemblyViewModel.StepOverCommand.CanExecute(null).Should().BeTrue(
                "step over should be available while paused");

            long initialCycles = state.Cycles;
            disassemblyViewModel.StepOverCommand.Execute(null);

            StepIntoAsmUiTests.WaitUntil(
                () => pauseHandler.IsPaused
                      && disassemblyViewModel.IsPaused
                      && state.IpSegmentedAddress == expectedAddress,
                timeoutMilliseconds: 5000,
                failureMessage: "step over should pause at the expected destination");

            if (assertSingleInstructionCycleDelta) {
                state.Cycles.Should().Be(initialCycles + 1,
                    "step over should execute exactly one instruction for non-call instructions");
            }

            breakpointsViewModel.Breakpoints.Should().BeEmpty(
                "step over must not create or leave temporary UI breakpoints");
        } finally {
            state.IsRunning = false;
            pauseHandler.Resume();
            StepIntoAsmUiTests.WaitUntil(
                () => runTask.IsCompleted,
                timeoutMilliseconds: 5000,
                failureMessage: "The emulation task should complete during cleanup");

            if (runTask.IsFaulted) {
                await runTask;
            }

            ProcessUiEvents();
        }
    }
}
