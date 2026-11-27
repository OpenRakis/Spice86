namespace Spice86.Tests.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Naming;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

/// <summary>
/// The generator front-end objects the effect-builder tests assemble around a program: renderer,
/// context, plan, transfer emitter and effect builder.
/// </summary>
internal sealed record EffectBuilderSetup(AstInstructionRenderer Renderer, CfgGeneratorContext Context,
    GenerationPlan Plan, TransferEmitter Transfer, HardwareInterruptEffectBuilder Effects) {
    /// <summary>Builds the front-end of <paramref name="program"/> under the strict policy,
    /// without computing the effect.</summary>
    internal static EffectBuilderSetup Create(CfgPartitionedProgram program) {
        AstInstructionRenderer renderer = new(AsmRenderingConfig.CreateSpice86Style());
        GeneratorAnalysis analysis = GeneratorAnalysis.Build(program,
            new InstructionMnemonic(renderer), GenerationPolicy.Strict);
        CfgGeneratorContext context = analysis.Context;
        GenerationPlan plan = GenerationPlanBuilder.Build(context);
        TransferEmitter transfer = new(context);
        HardwareInterruptEffectBuilder effects = new(context, transfer, renderer, plan);
        return new EffectBuilderSetup(renderer, context, plan, transfer, effects);
    }
}
