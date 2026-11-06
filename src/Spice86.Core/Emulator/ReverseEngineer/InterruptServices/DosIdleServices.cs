namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Dos;

/// <summary>
/// Typed helpers for the INT 28h services.
/// </summary>
public sealed class DosIdleServices : InterruptServiceGroup<DosInt28Handler> {
    /// <summary>
    /// Initializes a new instance of the <see cref="DosIdleServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public DosIdleServices(CSharpOverrideHelper helper) : base(helper) {
    }

    /// <summary>
    /// INT 28h: DOS idle interrupt issued while DOS waits for input; the default handler does nothing.
    /// </summary>
    public void Idle(ushort expectedReturnCs, ushort expectedReturnIp) =>
        CallDirect(expectedReturnCs, expectedReturnIp, nameof(Idle), Handler.Idle);
}
