namespace Spice86.Emulator.InterruptHandlers.Bios;

using Spice86.Emulator.Machine;

/// <summary>
/// Very basic implementation of int 11 that basically does nothing.
/// </summary>
public class BiosEquipmentDeterminationInt11Handler : InterruptHandler
{
    public BiosEquipmentDeterminationInt11Handler(Machine machine) : base(machine)
    {
    }

    public override int GetIndex()
    {
        return 0x11;
    }

    public override void Run()
    {
        _state.SetAX(0);
    }
}