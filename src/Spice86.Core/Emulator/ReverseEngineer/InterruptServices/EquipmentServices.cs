namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Typed helpers for the INT 11h services.
/// </summary>
public sealed class EquipmentServices : InterruptServiceGroup {
    /// <summary>
    /// Initializes a new instance of the <see cref="EquipmentServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public EquipmentServices(CSharpOverrideHelper helper) : base(helper, 0x11, null) {
    }

    /// <summary>
    /// INT 11h: returns the BIOS equipment list in AX.
    /// </summary>
    public void GetEquipmentList(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x00);
    }
}
