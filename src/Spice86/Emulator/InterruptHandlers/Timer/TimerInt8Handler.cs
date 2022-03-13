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
        _pic = machine.Pic;
        _timer = machine.Timer;
        _memory = machine.Memory;
    }

    public override byte Index => 0x8;

    public uint TickCounterValue { get => _memory.GetUint32(BIOS_DATA_AREA_OFFSET_TICK_COUNTER_ADDRESS); set => _memory.SetUint32(BIOS_DATA_AREA_OFFSET_TICK_COUNTER_ADDRESS, value); }

    public override void Run() {
        long numberOfTicks = _timer.NumberOfTicks;
        TickCounterValue = (uint)numberOfTicks;
        int irq = _pic.AcknwowledgeInterruptRequest();
        // Avoid Timer and Keyboard, which RaiseIRQ and Process the interrupt vector themselves. 
        if (irq >= 0 && irq != 8 && irq != 9) {
            uint? vector = _pic.RaiseHardwareInterruptRequest((byte)irq);
            if (vector is not null) {
                _pic.ProcessInterruptVector((byte)vector);
            }
        }
    }
}