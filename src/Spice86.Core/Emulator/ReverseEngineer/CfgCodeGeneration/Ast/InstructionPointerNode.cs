namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>The <c>IP</c> register, printed by name.</summary>
internal sealed record InstructionPointerNode : ValueNode {
    /// <summary>Initializes a new instance of the <see cref="InstructionPointerNode"/> record.</summary>
    public InstructionPointerNode() : base(DataType.UINT16) {
    }

    /// <inheritdoc />
    public override T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitInstructionPointerNode(this);
}
