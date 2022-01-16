namespace Spice86.Emulator.InterruptHandlers.Timer;

using Spice86.Emulator.Devices.ExternalInput;
using Spice86.Emulator.Devices.Timer;
using Spice86.Emulator.Machine;
using Spice86.Emulator.Memory;

/// <summary>
/// Implementation of int8 that just updates a value in the bios data area.
/// </summary>
public class TimerInt8Handler : InterruptHandler {
    private static readonly int BIOS_DATA_AREA_OFFSET_TICK_COUNTER_ADDRESS = MemoryUtils.ToPhysicalAddress(MemoryMap.BiosDataAreaSegment, MemoryMap.BiosDataAreaOffsetTickCounter);
    private Pic pic;
    private Timer timer;

    public TimerInt8Handler(Machine machine) : base(machine) {
        timer = machine.GetTimer();
        _memory = machine.GetMemory();
        pic = machine.GetPic();
    }

    public override int GetIndex() {
        return 0x8;
    }

    public int GetTickCounterValue() {
        return (int)_memory.GetUint32(BIOS_DATA_AREA_OFFSET_TICK_COUNTER_ADDRESS);
    }

    public override void Run() {
        long numberOfTicks = timer.GetNumberOfTicks();
        SetTickCounterValue((int)numberOfTicks);
        pic.AcknwowledgeInterrupt();
    }

    public void SetTickCounterValue(int value) {
        _memory.SetUint32(BIOS_DATA_AREA_OFFSET_TICK_COUNTER_ADDRESS, (uint)value);
    }
}