namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Timer;

/// <summary>
/// Typed helpers for the INT 1Ch services.
/// </summary>
public sealed class UserTimerServices : InterruptServiceGroup<DummyInt1CHandler> {
    /// <summary>
    /// Initializes a new instance of the <see cref="UserTimerServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public UserTimerServices(CSharpOverrideHelper helper) : base(helper) {
    }

    /// <summary>
    /// INT 1Ch: user timer tick invoked on every timer tick; the default handler returns immediately.
    /// </summary>
    public void Tick(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, nameof(Tick));
    }

    /// <summary>
    /// Runs the user timer tick directly, without an interrupt frame.
    /// </summary>
    public void Tick() {
        Handler.Tick();
    }
}
