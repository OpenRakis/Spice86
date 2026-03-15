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
using Spice86.Logging;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels;
using Spice86.ViewModels.Services;

public class DisassemblyViewUiTests : BreakpointUiTestBase {
    private DisassemblyViewModelContext CreateDisassemblyViewModelContext() {
        ILoggerService loggerService = CreateMockLoggerService();
        PauseHandler pauseHandler = CreatePauseHandler(loggerService);
        State state = CreateState();
        state.CS = 0xF000;
        state.IP = 0xFFF0;

        MemoryContext memoryContext = CreateMemory();
        EmulatorBreakpointsManager emulatorBreakpointsManager =
            CreateBreakpointsManager(
                pauseHandler,
                state,
                memoryContext.Memory,
                memoryContext.MemoryBreakpoints,
                memoryContext.IoBreakpoints);

        UIDispatcher uiDispatcher = CreateUIDispatcher();
        IMessenger messenger = CreateMessenger();
        ITextClipboard textClipboard = Substitute.For<ITextClipboard>();

        BreakpointsViewModel breakpointsViewModel = new(
            state,
            pauseHandler,
            messenger,
            emulatorBreakpointsManager,
            uiDispatcher,
            textClipboard,
            memoryContext.Memory);

        IDictionary<SegmentedAddress, FunctionInformation> functionsInformation =
            new Dictionary<SegmentedAddress, FunctionInformation>();

        DisassemblyViewModel viewModel = new(
            emulatorBreakpointsManager,
            memoryContext.Memory,
            state,
            functionsInformation,
            breakpointsViewModel,
            pauseHandler,
            uiDispatcher,
            messenger,
            textClipboard,
            loggerService);

        return new DisassemblyViewModelContext(viewModel, pauseHandler);
    }

    [AvaloniaFact]
    public void DisassemblyViewModel_Activated_BreakpointPauseShouldUpdateUiState() {
        DisassemblyViewModelContext context = CreateDisassemblyViewModelContext();

        try {
            context.ViewModel.Activate();
            ProcessUiEvents();

            context.PauseHandler.RequestPause("Execution breakpoint reached");
            ProcessUiEvents();

            context.ViewModel.IsPaused.Should().BeTrue("the disassembly view model should react to breakpoint pause events");
        } finally {
            ResumeIfPaused(context.PauseHandler);
        }
    }

    [AvaloniaFact]
    public void DisassemblyViewModel_ReactivatedAfterResume_RefreshesPausedState() {
        DisassemblyViewModelContext context = CreateDisassemblyViewModelContext();

        try {
            context.ViewModel.Activate();
            ProcessUiEvents();

            context.PauseHandler.RequestPause("Pause for stale state check");
            ProcessUiEvents();

            context.ViewModel.IsPaused.Should().BeTrue("the view model should reflect pause while active");

            context.ViewModel.Deactivate();
            ProcessUiEvents();

            context.PauseHandler.Resume();
            ProcessUiEvents();

            context.ViewModel.IsPaused.Should().BeTrue("while inactive it does not receive resumed events and can keep stale state");

            context.ViewModel.Activate();
            ProcessUiEvents();

            context.ViewModel.IsPaused.Should().BeFalse("reactivating should resynchronize with the current pause handler state");
        } finally {
            ResumeIfPaused(context.PauseHandler);
        }
    }

    [AvaloniaFact]
    public void DisassemblyViewModel_QueuedResumeThenImmediatePause_KeepsPausedState() {
        DisassemblyViewModelContext context = CreateDisassemblyViewModelContext();

        try {
            context.ViewModel.Activate();
            ProcessUiEvents();

            context.PauseHandler.RequestPause("Initial pause before ordering test");
            ProcessUiEvents();

            context.ViewModel.IsPaused.Should().BeTrue("sanity check: the view model should be paused before ordering test");

            context.PauseHandler.Resume();
            context.PauseHandler.RequestPause("Immediate re-pause after resume");

            ProcessUiEvents();

            context.ViewModel.IsPaused.Should().BeTrue(
                "a queued resumed callback must not override a newer paused state");
        } finally {
            ResumeIfPaused(context.PauseHandler);
        }
    }

    private static void ResumeIfPaused(PauseHandler pauseHandler) {
        if (pauseHandler.IsPaused) {
            pauseHandler.Resume();
            ProcessUiEvents();
        }
    }

    private sealed class DisassemblyViewModelContext {
        public DisassemblyViewModelContext(DisassemblyViewModel viewModel, PauseHandler pauseHandler) {
            ViewModel = viewModel;
            PauseHandler = pauseHandler;
        }

        public DisassemblyViewModel ViewModel { get; }

        public PauseHandler PauseHandler { get; }
    }

}
