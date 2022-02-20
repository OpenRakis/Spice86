namespace Spice86.Emulator.InterruptHandlers.Timer;

using Spice86.Emulator.Devices.ExternalInput;
using Spice86.Emulator.Devices.Timer;
using Spice86.Emulator.VM;
using Spice86.Emulator.Memory;

/// <summary>
/// Implementation of int8 that just updates a value in the bios data area.
/// </summary>
public class TimerInt8Handler : InterruptHandler {
    private static readonly uint BIOS_DATA_AREA_OFFSET_TICK_COUNTER_ADDRESS = MemoryUtils.ToPhysicalAddress(MemoryMap.BiosDataAreaSegment, MemoryMap.BiosDataAreaOffsetTickCounter);
    private readonly Pic _pic;
    private readonly Timer _timer;

    public TimerInt8Handler(Machine machine) : base(machine) {
        _timer = machine.Timer;
        _memory = machine.Memory;
        _pic = machine.Pic;
    }

    public override byte Index => 0x8;

    public uint TickCounterValue => _memory.GetUint32(BIOS_DATA_AREA_OFFSET_TICK_COUNTER_ADDRESS);

    public override void Run() {
        long numberOfTicks = _timer.NumberOfTicks;
        SetTickCounterValue((uint)numberOfTicks);
        _pic.AcknwowledgeInterrupt();
    }

    public void SetTickCounterValue(uint value) {
        _memory.SetUint32(BIOS_DATA_AREA_OFFSET_TICK_COUNTER_ADDRESS, value);
    }
}