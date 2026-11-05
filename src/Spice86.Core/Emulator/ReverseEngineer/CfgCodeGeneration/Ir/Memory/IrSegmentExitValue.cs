namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using System.Linq;

/// <summary>What a partition entry leaves in a register when it returns.</summary>
internal abstract record IrSegmentExitValue {
    private IrSegmentExitValue() {
    }

    /// <summary>The caller's value untouched: the write summary reports the register not
    /// written.</summary>
    internal sealed record Preserved : IrSegmentExitValue {
        public static readonly Preserved Instance = new();
    }

    /// <summary>A constant every return of the entry resolves to.</summary>
    internal sealed record Known(ushort Value) : IrSegmentExitValue;

    /// <summary>The register is written and no constant comes out of every return.</summary>
    internal sealed record Unknown : IrSegmentExitValue {
        public static readonly Unknown Instance = new();
    }
}
