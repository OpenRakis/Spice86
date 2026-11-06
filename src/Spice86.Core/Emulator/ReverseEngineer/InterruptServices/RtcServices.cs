namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Bios;

/// <summary>
/// Typed helpers for the INT 70h services.
/// </summary>
public sealed class RtcServices : InterruptServiceGroup<RtcInt70Handler> {
    /// <summary>
    /// Initializes a new instance of the <see cref="RtcServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public RtcServices(CSharpOverrideHelper helper) : base(helper) {
    }

    /// <summary>
    /// INT 70h: real-time clock (IRQ 8) interrupt; reads CMOS status register C and acknowledges the PIC.
    /// </summary>
    public void HandleInterrupt(ushort expectedReturnCs, ushort expectedReturnIp) {
        CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(HandleInterrupt), () => Handler.HandleInterrupt());
    }
}
