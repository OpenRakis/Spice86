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
        //Arrange
        ILoggerService loggerService = CreateMockLoggerService();
        PauseHandler pauseHandler = CreatePauseHandler(loggerService);
        State state = CreateState();
        MemoryContext memoryContext = CreateMemory();
        ControlledDispatcher uiDispatcher = new();

        using CpuViewModel viewModel = new(state, memoryContext.Memory, pauseHandler, uiDispatcher) {
            IsVisible = true
        };

        try {
            pauseHandler.RequestPause("Initial pause before ordering test");
            uiDispatcher.ExecuteAll();

            ushort editedAxValue = 0x1234;

            pauseHandler.Resume();
            pauseHandler.RequestPause("Immediate re-pause after resume");
            uiDispatcher.ExecuteLast();
            uiDispatcher.ExecuteFirst();

            //Act
            viewModel.UpdateValues(null, EventArgs.Empty);
            viewModel.State.AX = editedAxValue;

            //Assert
            state.AX.Should().Be(editedAxValue,
                "a queued resumed callback must not disable paused editing behavior after an immediate re-pause");
        } finally {
            viewModel.IsVisible = false;
            uiDispatcher.ExecuteAll();
        }
    }

    [AvaloniaFact]
    public void MemoryViewModel_QueuedResumeThenImmediatePause_KeepsPausedState() {
        //Arrange
        MemoryViewModelContext context = CreateMemoryViewModelWithControlledDispatcher();

        context.PauseHandler.RequestPause("Initial pause before ordering test");
        context.Dispatcher.ExecuteAll();

        //Act
        context.PauseHandler.Resume();
        context.PauseHandler.RequestPause("Immediate re-pause after resume");
        context.Dispatcher.ExecuteLast();
        context.Dispatcher.ExecuteFirst();

        //Assert
        context.ViewModel.IsPaused.Should().BeTrue(
            "a queued resumed callback must not override a newer paused state");
    }

    private MemoryViewModelContext CreateMemoryViewModelWithControlledDispatcher() {
        ILoggerService loggerService = CreateMockLoggerService();
        PauseHandler pauseHandler = CreatePauseHandler(loggerService);
        State state = CreateState();
        MemoryContext memoryContext = CreateMemory();
        EmulatorBreakpointsManager breakpointsManager =
            CreateBreakpointsManager(
                pauseHandler,
                state,
                memoryContext.Memory,
                memoryContext.MemoryBreakpoints,
                memoryContext.IoBreakpoints);

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
            memoryContext.Memory);

        CallbackHandler callbackHandler = new(state, loggerService);
        Configuration configuration = new() {
            Exe = "test.exe"
        };
        MemoryDataExporter memoryDataExporter = new(memoryContext.Memory, callbackHandler, configuration, loggerService);

        IHostStorageProvider storageProvider = Substitute.For<IHostStorageProvider>();
        IStructureViewModelFactory structureViewModelFactory = Substitute.For<IStructureViewModelFactory>();

        MemoryViewModel viewModel = new(
            memoryContext.Memory,
            memoryDataExporter,
            state,
            breakpointsViewModel,
            pauseHandler,
            messenger,
            uiDispatcher,
            textClipboard,
            storageProvider,
            structureViewModelFactory);

        return new MemoryViewModelContext(viewModel, pauseHandler, uiDispatcher);
    }

    private sealed class MemoryViewModelContext {
        public MemoryViewModelContext(
            MemoryViewModel viewModel,
            PauseHandler pauseHandler,
            ControlledDispatcher dispatcher) {
            ViewModel = viewModel;
            PauseHandler = pauseHandler;
            Dispatcher = dispatcher;
        }

        public MemoryViewModel ViewModel { get; }

        public PauseHandler PauseHandler { get; }

        public ControlledDispatcher Dispatcher { get; }
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
