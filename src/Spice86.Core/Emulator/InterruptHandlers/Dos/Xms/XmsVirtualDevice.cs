using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;

namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;

public class XmsVirtualDevice : DefaultIOPortHandler {
    public XmsVirtualDevice(Machine machine, Configuration configuration) : base(machine, configuration) {
    }

    public override byte ReadByte(int port) => _machine.Memory.IsA20Enabled ? (byte)0x02 : (byte)0x00;

    public override void WriteByte(int port, byte value) => _machine.Memory.IsA20Enabled = (value & 0x02) != 0;
}