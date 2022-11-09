namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.Callback;

public class SystemBiosInt15Handler : InterruptHandler {
    public SystemBiosInt15Handler(Machine machine) : base(machine) {
        _dispatchTable.Add(0xC0, new Callback(0xC0, Unsupported));
        _dispatchTable.Add(0xC2, new Callback(0xC2, Unsupported));
        _dispatchTable.Add(0xC4, new Callback(0xC4, Unsupported));
    }

    public override byte Index => 0x15;

    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }

    private void Unsupported() {
        // We are not an IBM PS/2
        SetCarryFlag(true, true);
        _state.AH = 0x86;
    }
}