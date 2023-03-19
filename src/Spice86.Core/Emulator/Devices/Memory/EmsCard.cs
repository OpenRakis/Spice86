namespace Spice86.Core.Emulator.Devices.Memory;

using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Basic implementation of an EMS Memory add-on card (the RAM 3000 Deluxe).
/// </summary>
public class EmsCard : DefaultIOPortHandler {
    public EmsCard(Machine machine, Configuration configuration, ILoggerService loggerService) : base(machine, configuration, loggerService) {
        var ram = new Ram(6 * 1024 * 1024);
        ExpandedMemory = new Memory(ram);
        machine.Memory.RegisterMapping(0xE0000, 0x10000, ram);

    }

    public Memory ExpandedMemory { get; }

}