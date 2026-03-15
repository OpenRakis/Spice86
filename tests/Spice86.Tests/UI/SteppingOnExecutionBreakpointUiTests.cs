namespace Spice86.Tests.UI;

using Avalonia.Headless.XUnit;

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

        using Spice86DependencyInjection dependencyInjection = new Spice86Creator(
            "jump1",
            enablePit: false,
            maxCycles: 2_000_000,
            installInterruptVectors: false).Create();

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
        AddressBreakPoint persistentExecutionBreakpoint = new(
            BreakPointType.CPU_EXECUTION_ADDRESS,
            initialLinearAddress,
            _ => {
                pauseHandler.RequestPause($"Persistent execution breakpoint reached at {initialAddress}");
                pauseHandler.WaitIfPaused();
            },
            false);

        emulatorBreakpointsManager.ToggleBreakPoint(persistentExecutionBreakpoint, on: true);

        Task runTask = Task.Run(() => dependencyInjection.ProgramExecutor.Run());

        try {
            StepIntoAsmUiTests.WaitUntil(
                () => pauseHandler.IsPaused && disassemblyViewModel.IsPaused,
                timeoutMilliseconds: 5000,
                failureMessage: "The emulator should pause on the persistent execution breakpoint before stepping");

            state.IpSegmentedAddress.Should().Be(initialAddress,
                "the persistent execution breakpoint should pause on the instruction under test");

            long initialCycles = state.Cycles;

            if (useStepInto) {
                disassemblyViewModel.StepIntoCommand.CanExecute(null).Should().BeTrue(
                    "step into should be available while paused");
                disassemblyViewModel.StepIntoCommand.Execute(null);
            } else {
                disassemblyViewModel.StepOverCommand.CanExecute(null).Should().BeTrue(
                    "step over should be available while paused");
                disassemblyViewModel.StepOverCommand.Execute(null);
            }

            StepIntoAsmUiTests.WaitUntil(
                () => pauseHandler.IsPaused && disassemblyViewModel.IsPaused && state.IpSegmentedAddress == expectedAddress,
                timeoutMilliseconds: 5000,
                failureMessage: "stepping should advance to the next instruction even with a persistent execution breakpoint at the current address");

            state.Cycles.Should().Be(initialCycles + 1,
                "stepping should execute exactly one instruction");
        } finally {
            state.IsRunning = false;
            pauseHandler.Resume();
            StepIntoAsmUiTests.WaitUntil(
                () => runTask.IsCompleted,
                timeoutMilliseconds: 5000,
                failureMessage: "The emulation task should complete during cleanup");

            runTask.IsFaulted.Should().BeFalse("cleanup should not leave a faulted background run task");
            ProcessUiEvents();
        }
    }
}
