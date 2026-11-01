namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>The statement writing a register local back to its register, such as <c>AX = ax;</c>,
/// emitted where a promoted web's register is still read by code outside the web.</summary>
/// <param name="Register">The register written back.</param>
/// <param name="Local">The local holding its value; must be a local of <paramref name="Register"/>.</param>
/// <exception cref="ArgumentException"><paramref name="Local"/> is a local of another register
/// (different register index or width).</exception>
/// <remarks>
/// Analysis rules: <see cref="IrEffectsVisitor"/> gives it the effects of an assignment of the local to
/// the register - it must-writes the register's bytes and reads nothing (the local read is free). In
/// <see cref="IrNodeTyper"/> it is typed as that same assignment: the register and the local are typed
/// as its children. For <see cref="IrAstChildren"/> its children are the register and the local in that
/// order. It completes normally (<see cref="CompletesNormallyVisitor"/>), targets no label
/// (<see cref="GotoTargetCollector"/>) and joins no "never remove" list: a dead write-back may go, like a
/// dead register write. Passes pattern-matching
/// <see cref="BinaryOperationNode"/> <c>ASSIGN</c> with a <see cref="RegisterNode"/> left side do not see
/// it, which is conservative. No pass emits it yet, so printing it throws.
/// </remarks>
internal sealed record RegisterWriteBackNode : IVisitableAstNode {
    /// <summary>Creates a write-back of <paramref name="local"/> into <paramref name="register"/>.</summary>
    public RegisterWriteBackNode(RegisterNode register, RegisterLocalNode local) {
        if (local.Register != register) {
            throw new ArgumentException(
                $"Cannot write back local {local.VariableName} of {local.Register} into {register}.",
                nameof(local));
        }
        Register = register;
        Local = local;
    }

    /// <summary>The register written back.</summary>
    public RegisterNode Register { get; }

    /// <summary>The local holding the register's value.</summary>
    public RegisterLocalNode Local { get; }

    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitRegisterWriteBackNode(this);
}
