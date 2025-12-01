namespace Spice86.Tests.UI;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.Messaging;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Logging;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels;
using Spice86.ViewModels.Services;
using Spice86.Views;

/// <summary>
/// Base class for UI breakpoint tests providing shared infrastructure.
/// </summary>
public abstract class BreakpointUiTestBase {
    /// <summary>
    /// Creates a mocked logger service.
    /// </summary>
    protected static ILoggerService CreateMockLoggerService() {
        return Substitute.For<ILoggerService>();
    }

    /// <summary>
    /// Creates a mocked pause handler.
    /// </summary>
    protected static IPauseHandler CreateMockPauseHandler() {
        return Substitute.For<IPauseHandler>();
    }

    /// <summary>
    /// Creates a CPU State for testing.
    /// </summary>
    protected static State CreateState() {
        return new State(CpuModel.INTEL_80286);
    }

    /// <summary>
    /// Creates Memory for testing.
    /// </summary>
    protected static (Memory memory, AddressReadWriteBreakpoints memoryBreakpoints, AddressReadWriteBreakpoints ioBreakpoints) CreateMemory() {
        IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
        AddressReadWriteBreakpoints memoryBreakpoints = new();
        AddressReadWriteBreakpoints ioBreakpoints = new();
        A20Gate a20Gate = new(enabled: false);
        Memory memory = new(memoryBreakpoints, ram, a20Gate, initializeResetVector: true);
        return (memory, memoryBreakpoints, ioBreakpoints);
    }

    /// <summary>
    /// Creates an EmulatorBreakpointsManager for testing.
    /// </summary>
    protected static EmulatorBreakpointsManager CreateBreakpointsManager(
        IPauseHandler pauseHandler,
        State state,
        Memory memory,
        AddressReadWriteBreakpoints memoryBreakpoints,
        AddressReadWriteBreakpoints ioBreakpoints) {
        return new EmulatorBreakpointsManager(pauseHandler, state, memory, memoryBreakpoints, ioBreakpoints);
    }

    /// <summary>
    /// Creates a BreakpointsViewModel for testing.
    /// </summary>
    protected static BreakpointsViewModel CreateBreakpointsViewModel(
        State state,
        Memory memory,
        EmulatorBreakpointsManager breakpointsManager,
        ILoggerService loggerService,
        IPauseHandler pauseHandler) {
        IUIDispatcher uiDispatcher = Substitute.For<IUIDispatcher>();
        uiDispatcher.When(x => x.Post(Arg.Any<Action>())).Do(x => x.Arg<Action>()());

        ITextClipboard textClipboard = Substitute.For<ITextClipboard>();
        IMessenger messenger = Substitute.For<IMessenger>();

        return new BreakpointsViewModel(
            state,
            pauseHandler,
            messenger,
            breakpointsManager,
            uiDispatcher,
            textClipboard,
            memory);
    }

    /// <summary>
    /// Processes pending UI events.
    /// </summary>
    protected static void ProcessUiEvents() {
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Shows a window and waits for it to be ready.
    /// </summary>
    protected static void ShowWindowAndWait(Window window) {
        window.Show();
        ProcessUiEvents();
    }

    /// <summary>
    /// Creates a BreakpointsView with a properly configured ViewModel.
    /// </summary>
    protected static (BreakpointsView view, BreakpointsViewModel viewModel) CreateBreakpointsViewWithViewModel() {
        State state = CreateState();
        (Memory memory, AddressReadWriteBreakpoints memoryBreakpoints, AddressReadWriteBreakpoints ioBreakpoints) = CreateMemory();
        ILoggerService loggerService = CreateMockLoggerService();
        IPauseHandler pauseHandler = CreateMockPauseHandler();
        EmulatorBreakpointsManager breakpointsManager = CreateBreakpointsManager(
            pauseHandler, state, memory, memoryBreakpoints, ioBreakpoints);

        BreakpointsViewModel viewModel = CreateBreakpointsViewModel(
            state, memory, breakpointsManager, loggerService, pauseHandler);

        BreakpointsView view = new() {
            DataContext = viewModel
        };

        return (view, viewModel);
    }
}
