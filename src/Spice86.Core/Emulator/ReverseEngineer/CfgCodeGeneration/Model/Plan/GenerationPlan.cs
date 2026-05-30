namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;

/// <summary>
/// The source-level decisions the generator emits, computed up front so writing stays mechanical
/// ("plan then write"): segment field declarations, override registrations, and ordered per-partition method
/// plans. Built by <see cref="GenerationPlanBuilder"/>.
/// </summary>
internal sealed class GenerationPlan {
    public required IReadOnlyList<SegmentFieldPlan> SegmentFields { get; init; }
    public required IReadOnlyList<OverrideRegistration> OverrideRegistrations { get; init; }
    public required IReadOnlyList<MethodPlan> Methods { get; init; }
}
