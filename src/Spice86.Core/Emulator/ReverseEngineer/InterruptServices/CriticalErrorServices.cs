namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Typed helpers for the INT 24h services.
/// </summary>
public sealed class CriticalErrorServices : InterruptServiceGroup {
    /// <summary>
    /// Initializes a new instance of the <see cref="CriticalErrorServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public CriticalErrorServices(CSharpOverrideHelper helper) : base(helper, 0x24, null) {
    }

    /// <summary>
    /// INT 24h: default critical error handler; returns FAIL (AL=03h).
    /// </summary>
    public void HandleCriticalError(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x00);
    }
}
