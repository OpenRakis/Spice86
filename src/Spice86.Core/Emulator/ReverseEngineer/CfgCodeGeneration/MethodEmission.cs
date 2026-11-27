namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Data;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;

/// <summary>What emitting one method produced: its censuses and its constant-offset memory
/// accesses, the latter always collected for the globals the generator will address.</summary>
internal sealed record MethodEmission(MethodCensus Census, ConstantMemoryAccessTable ConstantMemoryAccesses);

/// <summary>A method whose IR was lifted, passed and structured, ready to print once the global
/// property names of the whole program are resolved.</summary>
internal sealed record PreparedMethod(
    MethodPlan Method,
    IrMethod IrMethod,
    IrSsa Ssa,
    IReadOnlyDictionary<ICfgNode, DosCallRecord> DosCalls,
    BlockNode EntryDispatch,
    BlockNode Body,
    GotoTargets Targets,
    MethodEmission Emission,
    Ir.Memory.FrameSlotPlan? FramePlan,
    SaveRestorePairs.SavedPairPlan SavedPairs,
    Ir.Memory.IrSegmentFacts EntryFacts,
    GenerationPolicy Policy) {
    /// <summary>The neutral stack-slot analysis of this preparation. It is lazy because most
    /// methods need no slots, and thread-safe because the two conventions fixed points can read it
    /// at the same time.</summary>
    internal Lazy<IrStackSlots> StackSlots { get; } = new(() =>
        IrCallingConventions.NeedsStackSlots(IrMethod)
            ? IrStackSlots.Compute(IrMethod,
                new IrMemoryAliasAnalysis(IrMethod, Ssa, EntryFacts, Policy), _ => null)
            : IrStackSlots.Empty);
}
