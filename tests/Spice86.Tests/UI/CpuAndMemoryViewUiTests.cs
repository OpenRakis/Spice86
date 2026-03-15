namespace Spice86.Tests.UI;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.Messaging;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels;
using Spice86.ViewModels.Services;
using Spice86.Shared.Utils;

public class CpuAndMemoryViewUiTests : BreakpointUiTestBase {
    [AvaloniaFact]
    public void CpuViewModel_QueuedResumeThenImmediatePause_KeepsPausedEditingBehavior() {
        // Arrange
        ILoggerService loggerService = CreateMockLoggerService();
        PauseHandler pauseHandler = CreatePauseHandler(loggerService);
        State state = CreateState();
        (Memory memory, AddressReadWriteBreakpoints memoryBreakpoints, AddressReadWriteBreakpoints ioBreakpoints) = CreateMemory();
        ControlledDispatcher uiDispatcher = new();

        CpuViewModel viewModel = new(state, memory, pauseHandler, uiDispatcher) {
            IsVisible = true
        };

        pauseHandler.RequestPause("Initial pause before ordering test");
        uiDispatcher.ExecuteAll();

        ushort editedAxValue = 0x1234;

        // Act
        pauseHandler.Resume();
        pauseHandler.RequestPause("Immediate re-pause after resume");
        uiDispatcher.ExecuteLast();
        uiDispatcher.ExecuteFirst();

        viewModel.UpdateValues(null, EventArgs.Empty);
        viewModel.State.AX = editedAxValue;

        // Assert
        state.AX.Should().Be(editedAxValue,
            "a queued resumed callback must not disable paused editing behavior after an immediate re-pause");
    }

    [AvaloniaFact]
    public void MemoryViewModel_QueuedResumeThenImmediatePause_KeepsPausedState() {
        // Arrange
        (MemoryViewModel viewModel, PauseHandler pauseHandler, ControlledDispatcher uiDispatcher) =
            CreateMemoryViewModelWithControlledDispatcher();

        pauseHandler.RequestPause("Initial pause before ordering test");
        uiDispatcher.ExecuteAll();

        // Act
        pauseHandler.Resume();
        pauseHandler.RequestPause("Immediate re-pause after resume");
        uiDispatcher.ExecuteLast();
        uiDispatcher.ExecuteFirst();

        // Assert
        viewModel.IsPaused.Should().BeTrue(
            "a queued resumed callback must not override a newer paused state");
    }

    private (MemoryViewModel ViewModel, PauseHandler PauseHandler, ControlledDispatcher Dispatcher)
        CreateMemoryViewModelWithControlledDispatcher() {
        ILoggerService loggerService = CreateMockLoggerService();
        PauseHandler pauseHandler = CreatePauseHandler(loggerService);
        State state = CreateState();
        (Memory memory, AddressReadWriteBreakpoints memoryBreakpoints, AddressReadWriteBreakpoints ioBreakpoints) = CreateMemory();
        EmulatorBreakpointsManager breakpointsManager =
            CreateBreakpointsManager(pauseHandler, state, memory, memoryBreakpoints, ioBreakpoints);

        ControlledDispatcher uiDispatcher = new();
        IMessenger messenger = CreateMessenger();
        ITextClipboard textClipboard = Substitute.For<ITextClipboard>();

        BreakpointsViewModel breakpointsViewModel = new(
            state,
            pauseHandler,
            messenger,
            breakpointsManager,
            uiDispatcher,
            textClipboard,
            memory);

        CallbackHandler callbackHandler = new(state, loggerService);
        Configuration configuration = new() {
            Exe = "test.exe"
        };
        MemoryDataExporter memoryDataExporter = new(memory, callbackHandler, configuration, loggerService);

        IHostStorageProvider storageProvider = Substitute.For<IHostStorageProvider>();
        IStructureViewModelFactory structureViewModelFactory = Substitute.For<IStructureViewModelFactory>();

        MemoryViewModel viewModel = new(
            memory,
            memoryDataExporter,
            state,
            breakpointsViewModel,
            pauseHandler,
            messenger,
            uiDispatcher,
            textClipboard,
            storageProvider,
            structureViewModelFactory);

        return (viewModel, pauseHandler, uiDispatcher);
    }

    private sealed class ControlledDispatcher : IUIDispatcher {
        private readonly List<Action> _callbacks = [];

        public Task InvokeAsync(Action callback, DispatcherPriority priority = default) {
            callback();
            return Task.CompletedTask;
        }

        public void Post(Action callback, DispatcherPriority priority = default) {
            _callbacks.Add(callback);
        }

        public bool CheckAccess() {
            return true;
        }

        public void ExecuteAll() {
            while (_callbacks.Count > 0) {
                ExecuteFirst();
            }
        }

        public void ExecuteFirst() {
            Action callback = _callbacks[0];
            _callbacks.RemoveAt(0);
            callback();
        }

        public void ExecuteLast() {
            int lastIndex = _callbacks.Count - 1;
            Action callback = _callbacks[lastIndex];
            _callbacks.RemoveAt(lastIndex);
            callback();
        }
    }
}
