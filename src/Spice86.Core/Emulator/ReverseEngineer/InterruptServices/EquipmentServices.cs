namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Bios;

/// <summary>
/// Typed helpers for the INT 11h services.
/// </summary>
public sealed class EquipmentServices : InterruptServiceGroup<BiosEquipmentDeterminationInt11Handler> {
    /// <summary>
    /// Initializes a new instance of the <see cref="EquipmentServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public EquipmentServices(CSharpOverrideHelper helper) : base(helper) {
    }

    /// <summary>
    /// INT 11h: returns the BIOS equipment list in AX.
    /// </summary>
    public ushort GetEquipmentList(ushort expectedReturnCs, ushort expectedReturnIp) =>
        CallDirect(expectedReturnCs, expectedReturnIp, nameof(GetEquipmentList),
            Handler.GetEquipmentList);
}
