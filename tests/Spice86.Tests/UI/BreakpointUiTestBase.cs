namespace Spice86.Tests.UI;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

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
using Spice86.Views;

using System.Diagnostics;

/// <summary>
/// Base class for UI breakpoint tests providing shared infrastructure.
/// Only ILoggerService and ITextClipboard are mocked; all other components use real implementations.
/// </summary>
public abstract class BreakpointUiTestBase : IDisposable {
    private readonly List<PauseHandler> _pauseHandlers = new();

    /// <summary>
    /// Creates a mocked logger service. This is the only interface that should be mocked.
    /// </summary>
    protected static ILoggerService CreateMockLoggerService() {
        return Substitute.For<ILoggerService>();
    }

    /// <summary>
    /// Creates a real pause handler with a mocked logger.
    /// </summary>
    protected PauseHandler CreatePauseHandler(ILoggerService loggerService) {
        PauseHandler pauseHandler = new(loggerService);
        _pauseHandlers.Add(pauseHandler);
        return pauseHandler;
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
    /// Creates a real UIDispatcher using Avalonia's UI thread dispatcher.
    /// </summary>
    protected static UIDispatcher CreateUIDispatcher() {
        return new UIDispatcher(Dispatcher.UIThread);
    }

    /// <summary>
    /// Creates a real messenger instance.
    /// </summary>
    protected static IMessenger CreateMessenger() {
        return WeakReferenceMessenger.Default;
    }

    /// <summary>
    /// Creates a BreakpointsViewModel for testing.
    /// Uses real implementations where possible; only ILoggerService and ITextClipboard are mocked.
    /// </summary>
    protected BreakpointsViewModel CreateBreakpointsViewModel(
        State state,
        Memory memory,
        EmulatorBreakpointsManager breakpointsManager,
        ILoggerService loggerService) {
        PauseHandler pauseHandler = CreatePauseHandler(loggerService);
        UIDispatcher uiDispatcher = CreateUIDispatcher();
        IMessenger messenger = CreateMessenger();

        // ITextClipboard is mocked because it requires platform-specific clipboard access
        ITextClipboard textClipboard = Substitute.For<ITextClipboard>();

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
    /// Selects a breakpoint type tab by name and processes UI events.
    /// </summary>
    /// <param name="viewModel">The BreakpointsViewModel containing the tabs.</param>
    /// <param name="tabName">The name of the tab to select (e.g., "Execution", "Memory", "Cycles").</param>
    /// <returns>True if the tab was found and selected, false otherwise.</returns>
    protected static bool SelectBreakpointTab(BreakpointsViewModel viewModel, string tabName) {
        BreakpointTypeTabItemViewModel? tab = viewModel.BreakpointTabs.FirstOrDefault(t => t.Header == tabName);
        if (tab is null) {
            return false;
        }
        viewModel.SelectedBreakpointTypeTab = tab;
        ProcessUiEvents();
        return true;
    }

    /// <summary>
    /// Creates a BreakpointsView with a properly configured ViewModel.
    /// Uses real implementations where possible.
    /// </summary>
    protected (BreakpointsView view, BreakpointsViewModel viewModel) CreateBreakpointsViewWithViewModel() {
        State state = CreateState();
        (Memory memory, AddressReadWriteBreakpoints memoryBreakpoints, AddressReadWriteBreakpoints ioBreakpoints) = CreateMemory();
        ILoggerService loggerService = CreateMockLoggerService();
        PauseHandler pauseHandler = CreatePauseHandler(loggerService);
        EmulatorBreakpointsManager breakpointsManager = CreateBreakpointsManager(
            pauseHandler, state, memory, memoryBreakpoints, ioBreakpoints);

        BreakpointsViewModel viewModel = CreateBreakpointsViewModel(
            state, memory, breakpointsManager, loggerService);

        BreakpointsView view = new() {
            DataContext = viewModel
        };

        return (view, viewModel);
    }

    protected DisassemblySteppingContext CreateActiveDisassemblySteppingContext(Spice86DependencyInjection dependencyInjection) {
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

        return new DisassemblySteppingContext(
            state,
            pauseHandler,
            emulatorBreakpointsManager,
            breakpointsViewModel,
            disassemblyViewModel);
    }

    protected static AddressBreakPoint CreateExecutionPauseBreakpoint(
        SegmentedAddress address,
        IPauseHandler pauseHandler,
        bool removeOnTrigger) {
        long linearAddress = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset);
        return new AddressBreakPoint(
            BreakPointType.CPU_EXECUTION_ADDRESS,
            linearAddress,
            _ => {
                pauseHandler.RequestPause($"Execution breakpoint reached at {address}");
                pauseHandler.WaitIfPaused();
            },
            removeOnTrigger);
    }

    protected static void WaitUntil(Func<bool> condition, int timeoutMilliseconds, string failureMessage) {
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

    protected sealed class DisassemblySteppingContext {
        public DisassemblySteppingContext(
            State state,
            IPauseHandler pauseHandler,
            EmulatorBreakpointsManager emulatorBreakpointsManager,
            BreakpointsViewModel breakpointsViewModel,
            DisassemblyViewModel disassemblyViewModel) {
            State = state;
            PauseHandler = pauseHandler;
            EmulatorBreakpointsManager = emulatorBreakpointsManager;
            BreakpointsViewModel = breakpointsViewModel;
            DisassemblyViewModel = disassemblyViewModel;
        }

        public State State { get; }

        public IPauseHandler PauseHandler { get; }

        public EmulatorBreakpointsManager EmulatorBreakpointsManager { get; }

        public BreakpointsViewModel BreakpointsViewModel { get; }

        public DisassemblyViewModel DisassemblyViewModel { get; }
    }

    /// <summary>
    /// Disposes of test resources.
    /// </summary>
    public void Dispose() {
        foreach (PauseHandler pauseHandler in _pauseHandlers) {
            pauseHandler.Dispose();
        }
        _pauseHandlers.Clear();
        GC.SuppressFinalize(this);
    }
}
