namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

/// <summary>
/// The target a <see cref="GotoNode"/> or a <see cref="LabelNode"/> names: the identity a same-method jump
/// lands on. What a target is depends on the pipeline that lowered the method: a CFG node wrapper for the
/// <c>CfgBlock</c> pipeline, the block itself for the IR pipeline. Implementations must compare by the
/// identity they wrap (records get value equality automatically).
/// </summary>
internal interface IGotoTarget;
