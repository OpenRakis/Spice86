namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>The <c>loadOffset</c> parameter of a generated method, printed by name.</summary>
internal sealed record LoadOffsetParameterNode : ValueNode {
    /// <summary>Initializes a new instance of the <see cref="LoadOffsetParameterNode"/> record.</summary>
    public LoadOffsetParameterNode() : base(DataType.INT32) {
    }

    /// <inheritdoc />
    public override T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitLoadOffsetParameterNode(this);
}
