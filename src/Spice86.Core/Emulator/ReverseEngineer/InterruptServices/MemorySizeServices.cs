namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Typed helpers for the INT 12h services.
/// </summary>
public sealed class MemorySizeServices : InterruptServiceGroup {
    /// <summary>
    /// Initializes a new instance of the <see cref="MemorySizeServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public MemorySizeServices(CSharpOverrideHelper helper) : base(helper, 0x12, null) {
    }

    /// <summary>
    /// INT 12h: returns the conventional memory size in KB in AX.
    /// </summary>
    public void GetConventionalMemorySize(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x00);
    }
}
