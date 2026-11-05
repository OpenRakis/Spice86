namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using System.Linq;

/// <summary>What a call leaves in a register version it defines.</summary>
internal abstract record IrSegmentCallValue {
    private IrSegmentCallValue() {
    }

    /// <summary>The value the call read of the register: every callee preserves it.</summary>
    internal sealed record SameAsRead(int Version) : IrSegmentCallValue;

    /// <summary>A constant every callee leaves on return.</summary>
    internal sealed record Known(ushort Value) : IrSegmentCallValue;
}
