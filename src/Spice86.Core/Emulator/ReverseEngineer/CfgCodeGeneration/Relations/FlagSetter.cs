namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Relations;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// An ALU call seen as the setter of the flags a later condition reads: the operation name, its width and,
/// for shifts and rotates, the constant count when it is known.
/// </summary>
internal sealed record FlagSetter(AluOperation Operation, BitWidth Width, int? Count) {
    /// <summary>
    /// The setter of <paramref name="node"/>, or <c>null</c> when its operation has no flag relation rule.
    /// </summary>
    public static FlagSetter? Of(AluOperationNode node) {
        AluOperation operation = node.Operation;
        if (operation is not (AluOperation.Sub or AluOperation.And or AluOperation.Or or AluOperation.Xor
            or AluOperation.Add or AluOperation.Inc or AluOperation.Dec or AluOperation.Adc or AluOperation.Sbb
            or AluOperation.Shl or AluOperation.Shr or AluOperation.Sar or AluOperation.Rol
            or AluOperation.Ror)) {
            return null;
        }
        int? count = null;
        if (operation is AluOperation.Shl or AluOperation.Shr or AluOperation.Sar or AluOperation.Rol
                or AluOperation.Ror
            && node.Call.CallNode.Arguments.Count > 1) {
            count = Constant(node.Call.CallNode.Arguments[1]);
        }
        return new FlagSetter(operation, node.Width, count);
    }

    private static int? Constant(IVisitableAstNode node) => node switch {
        ConstantNode constant => (int)constant.Value,
        InstructionFieldNode field when field.ResolvedNode is ConstantNode resolved => (int)resolved.Value,
        _ => null
    };
}
