namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Dos;

/// <summary>
/// Typed helpers for the INT 2Ah services.
/// </summary>
public sealed class DosNetworkServices : InterruptServiceGroup<DosInt2aHandler> {
    /// <summary>
    /// Initializes a new instance of the <see cref="DosNetworkServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public DosNetworkServices(CSharpOverrideHelper helper) : base(helper) {
    }

    /// <summary>
    /// INT 2Ah AH=00h: reports in AH whether a network is installed; always reports none.
    /// </summary>
    public byte NetworkInstallationQuery(ushort expectedReturnCs, ushort expectedReturnIp) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(NetworkInstallationQuery), () => Handler.NetworkInstallationQuery());
    }
}
