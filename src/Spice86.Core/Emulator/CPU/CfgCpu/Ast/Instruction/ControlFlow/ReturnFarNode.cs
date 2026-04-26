namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

public class ReturnFarNode : CfgInstructionNode {
    public ReturnFarNode(CfgInstruction instruction, IVisitableAstNode bytesToPop, BitWidth retBitWidth) : base(instruction) {
        BytesToPop = bytesToPop;
        RetBitWidth = retBitWidth;
    }

    public IVisitableAstNode BytesToPop { get; }
    public BitWidth RetBitWidth { get; }

    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitReturnFarNode(this);
    }
}