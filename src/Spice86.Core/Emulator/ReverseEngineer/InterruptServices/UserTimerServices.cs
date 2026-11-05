namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Typed helpers for the INT 1Ch services.
/// </summary>
public sealed class UserTimerServices : InterruptServiceGroup {
    /// <summary>
    /// Initializes a new instance of the <see cref="UserTimerServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public UserTimerServices(CSharpOverrideHelper helper) : base(helper, 0x1C, null) {
    }

    /// <summary>
    /// INT 1Ch: user timer tick invoked on every timer tick; the default handler returns immediately.
    /// </summary>
    public void Tick(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x00);
    }
}
