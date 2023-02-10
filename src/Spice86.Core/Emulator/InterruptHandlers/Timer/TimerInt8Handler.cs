namespace Spice86.Core.Emulator.InterruptHandlers.Timer;
using Spice86.Core.Emulator.Devices.ExternalInput;

using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Implementation of int8 that just updates a value in the bios data area.
/// </summary>
public class TimerInt8Handler : InterruptHandler {
    private readonly DualPic _dualPic;
    private readonly Timer _timer;

    public TimerInt8Handler(Machine machine) : base(machine) {
        _timer = machine.Timer;
        _memory = machine.Memory;
        _dualPic = machine.DualPic;
    }

    public override byte Index => 0x8;

    public override void Run() {
        long numberOfTicks = _timer.NumberOfTicks;
        TickCounterValue = (uint)numberOfTicks;
        _dualPic.AcknwowledgeInterrupt();
    }

    public uint TickCounterValue {
        get => _machine.Bios.RealTimeClock; 
        set => _machine.Bios.RealTimeClock = value;
    }
}