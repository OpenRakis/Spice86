namespace Spice86.Core.Emulator.Devices.Memory; 

using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Basic implementation of an EMS Memory add-on card (the RAM 3000 Deluxe).
/// </summary>
public class EmsCard : DefaultIOPortHandler {
    public EmsCard(Machine machine, Configuration configuration) : base(machine, configuration)
    {
    }
    
    public Memory ExpandedMemory { get; init; } = new(6 * 1024);

}