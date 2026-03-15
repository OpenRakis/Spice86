namespace Spice86.Tests.UI;

using Avalonia.Controls;
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
using Spice86.Views;

public class DisassemblyViewUiTests : BreakpointUiTestBase {
    private (DisassemblyViewModel viewModel, PauseHandler pauseHandler) CreateDisassemblyViewModel() {
        ILoggerService loggerService = CreateMockLoggerService();
        PauseHandler pauseHandler = CreatePauseHandler(loggerService);
        State state = CreateState();
        state.CS = 0xF000;
        state.IP = 0xFFF0;

        (Memory memory, AddressReadWriteBreakpoints memoryBreakpoints, AddressReadWriteBreakpoints ioBreakpoints) = CreateMemory();
        EmulatorBreakpointsManager emulatorBreakpointsManager =
            CreateBreakpointsManager(pauseHandler, state, memory, memoryBreakpoints, ioBreakpoints);

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
            memory);

        IDictionary<SegmentedAddress, FunctionInformation> functionsInformation =
            new Dictionary<SegmentedAddress, FunctionInformation>();

        DisassemblyViewModel viewModel = new(
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

        return (viewModel, pauseHandler);
    }

    [AvaloniaFact]
    public void DisassemblyView_DataContextAssignedAfterAttach_BreakpointPauseShouldUpdateUiState() {
        // Arrange
        (DisassemblyViewModel updatedViewModel, PauseHandler updatedPauseHandler) = CreateDisassemblyViewModel();
        DisassemblyView view = new() {
            IsVisible = false
        };
        Window window = new() {
            Width = 1024,
            Height = 768,
            Content = view
        };

        try {
            ShowWindowAndWait(window);

            // Act
            view.DataContext = updatedViewModel;
            ProcessUiEvents();

            updatedPauseHandler.RequestPause("Execution breakpoint reached");
            ProcessUiEvents();

            // Assert
            updatedViewModel.IsPaused.Should().BeTrue("the disassembly view model should react to breakpoint pause events");
        } finally {
            ResumeIfPaused(updatedPauseHandler);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DisassemblyViewModel_ReactivatedAfterResume_RefreshesPausedState() {
        // Arrange
        (DisassemblyViewModel viewModel, PauseHandler pauseHandler) = CreateDisassemblyViewModel();

        try {
            // Act
            viewModel.Activate();
            ProcessUiEvents();

            pauseHandler.RequestPause("Pause for stale state check");
            ProcessUiEvents();

            viewModel.IsPaused.Should().BeTrue("the view model should reflect pause while active");

            viewModel.Deactivate();
            ProcessUiEvents();

            pauseHandler.Resume();
            ProcessUiEvents();

            viewModel.IsPaused.Should().BeTrue("while inactive it does not receive resumed events and can keep stale state");

            viewModel.Activate();
            ProcessUiEvents();

            // Assert
            viewModel.IsPaused.Should().BeFalse("reactivating should resynchronize with the current pause handler state");
        } finally {
            ResumeIfPaused(pauseHandler);
        }
    }

    [AvaloniaFact]
    public void DisassemblyViewModel_QueuedResumeThenImmediatePause_KeepsPausedState() {
        // Arrange
        (DisassemblyViewModel viewModel, PauseHandler pauseHandler) = CreateDisassemblyViewModel();

        try {
            // Act
            viewModel.Activate();
            ProcessUiEvents();

            pauseHandler.RequestPause("Initial pause before ordering test");
            ProcessUiEvents();

            viewModel.IsPaused.Should().BeTrue("sanity check: the view model should be paused before ordering test");

            // Queue a resumed UI update, then immediately pause again before draining the UI queue.
            pauseHandler.Resume();
            pauseHandler.RequestPause("Immediate re-pause after resume");

            ProcessUiEvents();

            // Assert
            viewModel.IsPaused.Should().BeTrue(
                "a queued resumed callback must not override a newer paused state");
        } finally {
            ResumeIfPaused(pauseHandler);
        }
    }

    private static void ResumeIfPaused(PauseHandler pauseHandler) {
        if (pauseHandler.IsPaused) {
            pauseHandler.Resume();
            ProcessUiEvents();
        }
    }

}
