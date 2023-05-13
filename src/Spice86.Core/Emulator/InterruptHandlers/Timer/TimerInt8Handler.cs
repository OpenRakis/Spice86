namespace Spice86.Core.Emulator.InterruptHandlers.Timer;
using Spice86.Core.Emulator.Devices.ExternalInput;

using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Implementation of int8 that just updates a value in the bios data area.
/// </summary>
public class TimerInt8Handler : InterruptHandler {
    private readonly DualPic _dualPic;
    private readonly Timer _timer;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public TimerInt8Handler(Machine machine, ILoggerService loggerService) : base(machine, loggerService) {
        _timer = machine.Timer;
        _memory = machine.Memory;
        _dualPic = machine.DualPic;
    }

    public override byte Index => 0x8;

    public override void Run() {
        long numberOfTicks = _timer.NumberOfTicks;
        TickCounterValue = (uint)numberOfTicks;
        _dualPic.AcknowledgeInterrupt();
    }

    public uint TickCounterValue {
        get => _machine.Bios.RealTimeClock; 
        set => _machine.Bios.RealTimeClock = value;
    }
}