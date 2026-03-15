namespace Spice86.Tests.UI;

using Avalonia.Headless.XUnit;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Shared.Diagnostics;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels;
using Spice86.ViewModels.Services;

public class MainWindowViewModelUiTests : BreakpointUiTestBase {
    [AvaloniaFact]
    public void MainWindowViewModel_CreatedWhilePaused_InitializesPausedStateFromPauseHandler() {
        //Arrange
        ILoggerService loggerService = CreateMockLoggerService();
        PauseHandler pauseHandler = CreatePauseHandler(loggerService);
        pauseHandler.RequestPause("Pause before creating main window view model");

        IUIDispatcher uiDispatcher = CreateUIDispatcher();
        ITimeMultiplier timeMultiplier = Substitute.For<ITimeMultiplier>();
        IHostStorageProvider hostStorageProvider = Substitute.For<IHostStorageProvider>();
        ITextClipboard textClipboard = Substitute.For<ITextClipboard>();
        IExceptionHandler exceptionHandler = Substitute.For<IExceptionHandler>();
        ICyclesLimiter cyclesLimiter = Substitute.For<ICyclesLimiter>();
        cyclesLimiter.TargetCpuCyclesPerMs.Returns(100);

        State state = new(CpuModel.INTEL_80286);
        PerformanceTracker performanceTracker = new(new SystemTimeProvider());
        PerformanceViewModel performanceViewModel = new(state, pauseHandler, uiDispatcher, performanceTracker);

        Configuration configuration = new() {
            Exe = "test.exe"
        };

        MainWindowViewModel viewModel = new(
            new SharedMouseData(),
            timeMultiplier,
            uiDispatcher,
            hostStorageProvider,
            textClipboard,
            configuration,
            loggerService,
            pauseHandler,
            performanceViewModel,
            exceptionHandler,
            cyclesLimiter);

        //Act
        ProcessUiEvents();

        //Assert
        viewModel.IsPaused.Should().BeTrue("the main window pause/play state must be correct even if it is created while already paused");
    }
}
