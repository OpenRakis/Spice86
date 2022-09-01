using Spice86.Core.Emulator.Devices.ExternalInput;

namespace Spice86.Core.Emulator.InterruptHandlers.Timer;

using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Implementation of int8 that just updates a value in the bios data area.
/// </summary>
public class TimerInt8Handler : InterruptHandler {
    private static readonly uint BIOS_DATA_AREA_OFFSET_TICK_COUNTER_ADDRESS = MemoryUtils.ToPhysicalAddress(MemoryMap.BiosDataAreaSegment, MemoryMap.BiosDataAreaOffsetTickCounter);
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

    public uint TickCounterValue { get => _memory.GetUint32(BIOS_DATA_AREA_OFFSET_TICK_COUNTER_ADDRESS); set => _memory.SetUint32(BIOS_DATA_AREA_OFFSET_TICK_COUNTER_ADDRESS, value); }
}