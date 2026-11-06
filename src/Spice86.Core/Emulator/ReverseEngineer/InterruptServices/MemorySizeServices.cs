namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Bios;

/// <summary>
/// Typed helpers for the INT 12h services.
/// </summary>
public sealed class MemorySizeServices : InterruptServiceGroup<SystemBiosInt12Handler> {
    /// <summary>
    /// Initializes a new instance of the <see cref="MemorySizeServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public MemorySizeServices(CSharpOverrideHelper helper) : base(helper) {
    }

    /// <summary>
    /// INT 12h: returns the conventional memory size in KB in AX.
    /// </summary>
    public ushort GetConventionalMemorySize(ushort expectedReturnCs, ushort expectedReturnIp) =>
        CallDirect(expectedReturnCs, expectedReturnIp, nameof(GetConventionalMemorySize),
            Handler.GetConventionalMemorySize);
}
