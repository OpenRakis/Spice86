namespace Spice86.Emulator.InterruptHandlers.Bios;

using Spice86.Emulator.Callback;
using Spice86.Emulator.VM;

public class SystemBiosInt15Handler : InterruptHandler {

    public SystemBiosInt15Handler(Machine machine) : base(machine) {
        _dispatchTable.Add(0xC0, new Callback(0xC0, Unsupported));
        _dispatchTable.Add(0xC2, new Callback(0xC2, Unsupported));
        _dispatchTable.Add(0xC4, new Callback(0xC4, Unsupported));
    }

    public override byte GetIndex() {
        return 0x15;
    }

    public override void Run() {
        byte operation = _state.GetAH();
        this.Run(operation);
    }

    private void Unsupported() {
        // We are not an IBM PS/2
        this.SetCarryFlag(true, true);
        _state.SetAH(0x86);
    }
}