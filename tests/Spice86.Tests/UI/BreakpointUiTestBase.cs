namespace Spice86.Tests.UI;

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

using System.Diagnostics;

public abstract class BreakpointUiTestBase : IDisposable {
    private readonly List<PauseHandler> _pauseHandlers = new();

    protected static ILoggerService CreateMockLoggerService() {
        return Substitute.For<ILoggerService>();
    }

    protected PauseHandler CreatePauseHandler(ILoggerService loggerService) {
        PauseHandler pauseHandler = new(loggerService);
        _pauseHandlers.Add(pauseHandler);
        return pauseHandler;
    }

    protected static State CreateState() {
        return new State(CpuModel.INTEL_80286);
    }

    protected static MemoryContext CreateMemory() {
        IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
        AddressReadWriteBreakpoints memoryBreakpoints = new();
        AddressReadWriteBreakpoints ioBreakpoints = new();
        A20Gate a20Gate = new(enabled: false);
        Memory memory = new(memoryBreakpoints, ram, a20Gate, initializeResetVector: true);
        return new MemoryContext(memory, memoryBreakpoints, ioBreakpoints);
    }

    protected static EmulatorBreakpointsManager CreateBreakpointsManager(
        IPauseHandler pauseHandler,
        State state,
        Memory memory,
        AddressReadWriteBreakpoints memoryBreakpoints,
        AddressReadWriteBreakpoints ioBreakpoints) {
        return new EmulatorBreakpointsManager(pauseHandler, state, memory, memoryBreakpoints, ioBreakpoints);
    }

    protected static UIDispatcher CreateUIDispatcher() {
        return new UIDispatcher(Dispatcher.UIThread);
    }

    protected static IMessenger CreateMessenger() {
        return WeakReferenceMessenger.Default;
    }

    protected static void ProcessUiEvents() {
        Dispatcher.UIThread.RunJobs();
    }

    protected static bool SelectBreakpointTab(BreakpointsViewModel viewModel, string tabName) {
        BreakpointTypeTabItemViewModel? tab = viewModel.BreakpointTabs.FirstOrDefault(t => t.Header == tabName);
        if (tab is null) {
            return false;
        }
        viewModel.SelectedBreakpointTypeTab = tab;
        ProcessUiEvents();
        return true;
    }

    protected BreakpointsContext CreateBreakpointsContext() {
        State state = CreateState();
        MemoryContext memoryContext = CreateMemory();
        ILoggerService loggerService = CreateMockLoggerService();
        PauseHandler pauseHandler = CreatePauseHandler(loggerService);
        EmulatorBreakpointsManager breakpointsManager = CreateBreakpointsManager(
            pauseHandler, state, memoryContext.Memory, memoryContext.MemoryBreakpoints, memoryContext.IoBreakpoints);
        IUIDispatcher uiDispatcher = CreateUIDispatcher();
        IMessenger messenger = CreateMessenger();
        ITextClipboard textClipboard = Substitute.For<ITextClipboard>();

        BreakpointsViewModel viewModel = new(
            state,
            pauseHandler,
            messenger,
            breakpointsManager,
            uiDispatcher,
            textClipboard,
            memoryContext.Memory);

        return new BreakpointsContext(state, memoryContext, pauseHandler, breakpointsManager, viewModel);
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

    protected sealed class MemoryContext {
        public MemoryContext(
            Memory memory,
            AddressReadWriteBreakpoints memoryBreakpoints,
            AddressReadWriteBreakpoints ioBreakpoints) {
            Memory = memory;
            MemoryBreakpoints = memoryBreakpoints;
            IoBreakpoints = ioBreakpoints;
        }

        public Memory Memory { get; }

        public AddressReadWriteBreakpoints MemoryBreakpoints { get; }

        public AddressReadWriteBreakpoints IoBreakpoints { get; }
    }

    protected sealed class BreakpointsContext {
        public BreakpointsContext(
            State state,
            MemoryContext memoryContext,
            PauseHandler pauseHandler,
            EmulatorBreakpointsManager breakpointsManager,
            BreakpointsViewModel breakpointsViewModel) {
            State = state;
            MemoryContext = memoryContext;
            PauseHandler = pauseHandler;
            BreakpointsManager = breakpointsManager;
            BreakpointsViewModel = breakpointsViewModel;
        }

        public State State { get; }

        public MemoryContext MemoryContext { get; }

        public PauseHandler PauseHandler { get; }

        public EmulatorBreakpointsManager BreakpointsManager { get; }

        public BreakpointsViewModel BreakpointsViewModel { get; }
    }

    public void Dispose() {
        foreach (PauseHandler pauseHandler in _pauseHandlers) {
            pauseHandler.Dispose();
        }
        _pauseHandlers.Clear();
        GC.SuppressFinalize(this);
    }
}
