namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;

/// <summary>
/// A method lifted to IR together with everything its old-pipeline counterpart needs: the emission
/// plan (labels and local suffixes), the instruction lowerer (old per-instruction lowering) and the
/// generator context (segment variables for printing).
/// </summary>
/// <param name="Method">The lifted method.</param>
/// <param name="Plan">The method plan it was lifted from.</param>
/// <param name="Lowerer">The instruction lowerer shared by the generation.</param>
/// <param name="Context">The generator context shared by the generation.</param>
internal sealed record LiftedMethod(IrMethod Method, MethodPlan Plan, InstructionLowerer Lowerer,
    CfgGeneratorContext Context);
