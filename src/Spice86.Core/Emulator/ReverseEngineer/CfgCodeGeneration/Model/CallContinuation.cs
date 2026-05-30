namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;

using Spice86.Shared.Emulator.Memory;

/// <summary>Planned post-call continuation for a call-like instruction (near/far call, interrupt call).</summary>
/// <param name="ExpectedReturnAddress">Address the CPU pushes and returns to (the instruction after the
/// call). Always known; the generated call helpers validate against it.</param>
/// <param name="ObservedContinuationEdge">CFG edge observed after the call returned, or <c>null</c> when the
/// callee never returned during discovery (the generator then fails as untested if that path runs).</param>
internal readonly record struct CallContinuation(
    SegmentedAddress ExpectedReturnAddress,
    ResolvedCfgEdge? ObservedContinuationEdge);
