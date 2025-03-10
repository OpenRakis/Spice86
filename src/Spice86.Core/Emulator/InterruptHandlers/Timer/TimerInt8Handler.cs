namespace Spice86.Core.Emulator.InterruptHandlers.Timer;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Implementation of int8 that just updates a value in the bios data area.
/// </summary>
public class TimerInt8Handler : InterruptHandler {
    private readonly DualPic _dualPic;
    private readonly Timer _timer;
    private readonly BiosDataArea _biosDataArea;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="biosDataArea">The memory mapped BIOS values.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="dualPic">The two programmable interrupt controllers.</param>
    /// <param name="timer">The programmable Interval Timer chip.</param>
    public TimerInt8Handler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state, DualPic dualPic, Timer timer, BiosDataArea biosDataArea, ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _timer = timer;
        Memory = memory;
        _dualPic = dualPic;
        _biosDataArea = biosDataArea;
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x8;

    /// <inheritdoc />
    public override void Run() {
        TickCounterValue++;
        _dualPic.AcknowledgeInterrupt(0);
    }

    /// <summary>
    /// Gets or set the value of the real time clock, in ticks.
    /// </summary>
    public uint TickCounterValue {
        get => _biosDataArea.TimerCounter;
        set => _biosDataArea.TimerCounter = value;
    }
}