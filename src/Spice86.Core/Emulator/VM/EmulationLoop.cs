namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Shared.Diagnostics;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.Diagnostics;

public class EmulationLoop {
    private readonly ILoggerService _loggerService;
    private readonly IInstructionExecutor _cpu;
    private readonly FunctionHandler _functionHandler;
    private readonly State _cpuState;
    private readonly Timer _timer;
    private readonly DeviceScheduler _scheduler;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly IPauseHandler _pauseHandler;
    private readonly PerformanceMeasurer _performanceMeasurer;
    private readonly Stopwatch _performanceStopwatch = new();
    private readonly DmaController _dmaController;
    private readonly CycleLimiterBase _cyclesLimiter;
    private readonly InputEventQueue _inputEventQueue;

    /// <summary>
    /// Gets or sets whether the emulation is paused.
    /// </summary>
    public bool IsPaused { get; set; }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="perfMeasurer">The class shared with the UI to update performance information.</param>
    /// <param name="functionHandler">The class that handles function calls in the machine code.</param>
    /// <param name="cpu">The emulated CPU, so the emulation loop can call ExecuteNextInstruction().</param>
    /// <param name="cpuState">The emulated CPU State, so that we know when to stop.</param>
    /// <param name="timer">The timer device, so the emulation loop can call Tick()</param>
    /// <param name="scheduler">The device scheduler, to handle event queue and device ticks.</param>
    /// <param name="emulatorBreakpointsManager">The class that stores emulation breakpoints.</param>
    /// <param name="dmaController">Used to perform DMA Channel data transfers regularly.</param>
    /// <param name="pauseHandler">The emulation pause handler.</param>
    /// <param name="cyclesLimiter">The class shared with the UI to control CPU speed.</param>
    /// <param name="inputEventQueue">Used to ensure that Mouse/Keyboard events are processed in the emulation thread.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public EmulationLoop(PerformanceMeasurer perfMeasurer,
        FunctionHandler functionHandler, IInstructionExecutor cpu, State cpuState,
        Timer timer, DeviceScheduler scheduler, EmulatorBreakpointsManager emulatorBreakpointsManager,
        DmaController dmaController, IPauseHandler pauseHandler,
        CycleLimiterBase cyclesLimiter, InputEventQueue inputEventQueue,
        ILoggerService loggerService) {
        _loggerService = loggerService;
        _dmaController = dmaController;
        _cpu = cpu;
        _functionHandler = functionHandler;
        _cpuState = cpuState;
        _timer = timer;
        _scheduler = scheduler;
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _pauseHandler = pauseHandler;
        _cyclesLimiter = cyclesLimiter;
        _performanceMeasurer = perfMeasurer;
        _inputEventQueue = inputEventQueue;
    }

    /// <summary>
    /// Starts and waits for the end of the emulation loop.
    /// </summary>
    /// <exception cref="InvalidVMOperationException">When an unhandled exception occurs. <br/>
    /// This can occur if the target program is not supported (yet).</exception>
    public void Run() {
        _emulatorBreakpointsManager.OnMachineStart();
        try {
            StartRunLoop(_functionHandler);
        } catch (HaltRequestedException) {
            // Actually a signal generated code requested Exit
            return;
        } catch (InvalidVMOperationException) {
            throw;
        } catch (Exception e) {
            throw new InvalidVMOperationException(_cpuState, e);
        }
        _emulatorBreakpointsManager.OnMachineStop();
        _cpu.SignalEnd();
    }

    /// <summary>
    /// Forces the emulation loop to exit.
    /// </summary>
    internal void Exit() {
        _cpuState.IsRunning = false;
        IsPaused = false;
    }

    private void StartRunLoop(FunctionHandler functionHandler) {
        // Entry could be overridden and could throw exceptions
        functionHandler.Call(CallType.MACHINE, _cpuState.IpSegmentedAddress, null, null, "entry", false);
        RunLoop();
        functionHandler.Ret(CallType.MACHINE, null);
    }

    private void RunLoop() {
        _performanceStopwatch.Start();
        _cpu.SignalEntry();
        while (_cpuState.IsRunning) {
            RunOnce();
        }
        _performanceStopwatch.Stop();
        OutputPerfStats();
    }

    private void RunOnce() {
        _emulatorBreakpointsManager.CheckExecutionBreakPoints();
        _pauseHandler.WaitIfPaused();

        // sub-ms queue before CPU executes
        _scheduler.RunEventQueue();

        _cpu.ExecuteNext();
        _performanceMeasurer.UpdateValue(_cpuState.Cycles);

        _timer.Tick();        // PIT IRQ0 if due
        _scheduler.DeviceTick(); // device periodic callbacks
        _dmaController.PerformDmaTransfers();
        _inputEventQueue.ProcessAllPendingInputEvents();
        _cyclesLimiter.RegulateCycles(_cpuState);
    }

    private void OutputPerfStats() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            long cyclesPerSeconds = _performanceMeasurer.AverageValuePerSecond;
            long elapsedTimeInMilliseconds = _performanceStopwatch.ElapsedMilliseconds;
            _loggerService.Warning(
                "Executed {Cycles} instructions in {ElapsedTimeMilliSeconds}ms. {CyclesPerSeconds} Instructions per seconds on average over run.",
                _cpuState.Cycles, elapsedTimeInMilliseconds, cyclesPerSeconds);
        }
    }

    internal void RunFromUntil(SegmentedAddress startAddress, SegmentedAddress endAddress) {
        _cpuState.IpSegmentedAddress = startAddress;
        while (_cpuState.IsRunning && _cpuState.IpSegmentedAddress != endAddress) {
            RunOnce();
        }
    }
}
