using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Spice86.Emulator.InterruptHandlers.Bios;

using Spice86.Emulator.Machine;
using Spice86.Emulator.Callback;

public class SystemBiosInt15Handler : InterruptHandler
{
    public SystemBiosInt15Handler(Machine machine) : base(machine)
    {
        _dispatchTable.Add(0xC0, new Callback(0xC0, () => Unsupported()));
        _dispatchTable.Add(0xC2, new Callback(0xC2, () => Unsupported()));
        _dispatchTable.Add(0xC4, new Callback(0xC4, () => Unsupported()));
    }

    public override void Run()
    {
        int operation = _state.GetAH();
        this.Run(operation);
    }

    public override int GetIndex()
    {
        return 0x15;
    }

    private void Unsupported()
    {

        // We are not an IBM PS/2
        base.SetCarryFlag(true, true);
        _state.SetAH(0x86);
    }
}
