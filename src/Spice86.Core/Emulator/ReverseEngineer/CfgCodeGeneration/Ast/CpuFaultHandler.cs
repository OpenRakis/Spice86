namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Shared.Emulator.Memory;

/// <summary>One handler a CPU fault can dispatch to: the handler address and the generated method entered there.</summary>
/// <param name="Handler">The IVT handler address the fault dispatches to.</param>
/// <param name="MethodName">The generated method entered at <paramref name="Handler"/>.</param>
/// <param name="LoadOffset">The load offset the method is entered with.</param>
internal sealed record CpuFaultHandler(SegmentedAddress Handler, string MethodName, int LoadOffset);
