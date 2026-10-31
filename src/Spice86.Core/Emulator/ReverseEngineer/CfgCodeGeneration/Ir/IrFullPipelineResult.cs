namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

/// <summary>What a full <see cref="IrPassPipeline"/> run produced: the SSA is always present because the
/// run went through both SSA builds.</summary>
/// <param name="Ssa">The SSA built before <see cref="IrPass.RepCompareLowering"/> and used by every
/// pass after it.</param>
/// <param name="Webs">The webs of that SSA as it was before <see cref="IrPass.DeadRegisterWrites"/>,
/// read by the web census; null when the run was not asked to build webs.</param>
internal sealed record IrFullPipelineResult(IrSsa Ssa, IrSsaWebs? Webs);
