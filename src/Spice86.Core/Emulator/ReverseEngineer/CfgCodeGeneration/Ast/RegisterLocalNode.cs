namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>A C# local holding the value of <see cref="Register"/> (at its width) inside a promoted
/// register web. Read like a value; assigned with
/// <see cref="BinaryOperationNode"/> <c>ASSIGN</c> whose left side is this node.</summary>
/// <param name="Register">The register the local stands for.</param>
/// <param name="VariableName">The C# name of the local.</param>
/// <param name="PrintedType">The C# type the local is declared and printed with: the register's
/// type, or the signed type of the same width when the plan voted the local signed.</param>
/// <remarks>
/// Analysis rules: reading the local has no effects (a local is not a machine value -
/// <see cref="IrEffectsVisitor"/> returns <see cref="IrEffects"/> <c>None</c>); assigning to it keeps
/// only the right side's effects (the <c>Assign</c> rule shared with
/// <see cref="VariableReferenceNode"/>). It is not a temporary: a web has several definitions, so it
/// deliberately does not derive from <see cref="VariableReferenceNode"/> and the temporary machinery
/// (<see cref="IrTemporaries"/>, the verifier's declaration tracking) never sees it. It is a leaf
/// for <see cref="IrAstChildren"/>, completes normally (<see cref="CompletesNormallyVisitor"/>) and
/// targets no label (<see cref="GotoTargetCollector"/>). The verifier treats it as a pure arm node.
/// The plan creates it only inside write-backs, which no IR pass sees.
/// </remarks>
internal sealed record RegisterLocalNode : ValueNode {
    /// <summary>Creates a local of <paramref name="register"/> printed with the register's own type.</summary>
    public RegisterLocalNode(RegisterNode register, string variableName)
        : this(register, variableName, register.DataType) {
    }

    /// <summary>Creates a local of <paramref name="register"/> printed with <paramref name="printedType"/>.</summary>
    public RegisterLocalNode(RegisterNode register, string variableName, DataType printedType)
        : base(printedType) {
        Register = register;
        VariableName = variableName;
    }

    /// <summary>The register the local stands for.</summary>
    public RegisterNode Register { get; }

    /// <summary>The C# name of the local.</summary>
    public string VariableName { get; }

    /// <inheritdoc />
    public override T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitRegisterLocalNode(this);
}
