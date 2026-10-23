namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Liveness;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

/// <summary>What flag liveness needs to know about one node.</summary>
/// <param name="Effect">The flags the node reads, always writes and may write.</param>
/// <param name="AllLiveBefore">Every status flag is live before the node (a CPU fault pushes FLAGS).</param>
/// <param name="AllLiveAfter">Every status flag is live after the node (control may reach code that reads any flag).</param>
internal readonly record struct InstructionFlagFacts(FlagEffect Effect, bool AllLiveBefore, bool AllLiveAfter);
