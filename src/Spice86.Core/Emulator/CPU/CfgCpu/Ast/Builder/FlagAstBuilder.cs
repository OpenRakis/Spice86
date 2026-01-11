namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>
/// Helper class for creating CPU flag nodes in the AST.
/// </summary>
public class FlagAstBuilder {
    /// <summary>
    /// Creates a CpuFlagNode for the Carry flag.
    /// </summary>
    public CpuFlagNode Carry() => new CpuFlagNode(Flags.Carry);

    /// <summary>
    /// Creates a CpuFlagNode for the Zero flag.
    /// </summary>
    public CpuFlagNode Zero() => new CpuFlagNode(Flags.Zero);

    /// <summary>
    /// Creates a CpuFlagNode for the Sign flag.
    /// </summary>
    public CpuFlagNode Sign() => new CpuFlagNode(Flags.Sign);

    /// <summary>
    /// Creates a CpuFlagNode for the Overflow flag.
    /// </summary>
    public CpuFlagNode Overflow() => new CpuFlagNode(Flags.Overflow);

    /// <summary>
    /// Creates a CpuFlagNode for the Parity flag.
    /// </summary>
    public CpuFlagNode Parity() => new CpuFlagNode(Flags.Parity);

    /// <summary>
    /// Creates a CpuFlagNode for the Auxiliary flag.
    /// </summary>
    public CpuFlagNode Auxiliary() => new CpuFlagNode(Flags.Auxiliary);

    /// <summary>
    /// Creates a CpuFlagNode for the Direction flag.
    /// </summary>
    public CpuFlagNode Direction() => new CpuFlagNode(Flags.Direction);

    /// <summary>
    /// Creates a CpuFlagNode for the Interrupt flag.
    /// </summary>
    public CpuFlagNode Interrupt() => new CpuFlagNode(Flags.Interrupt);

    /// <summary>
    /// Creates a CpuFlagNode for the Trap flag.
    /// </summary>
    public CpuFlagNode Trap() => new CpuFlagNode(Flags.Trap);
}
