namespace Spice86.Core.Emulator.CPU.CfgCpu;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;

public interface ICfgNodeVisitor {
    public ICfgNode? NextNode { get; }
    public void Accept(HltInstruction instruction);
    public void Accept(JmpNearImm8 instruction);
    public void Accept(JmpNearImm16 instruction);
    public void Accept(MovRegImm8 instruction);
    public void Accept(MovRegImm16 instruction);
    public void Accept(MovRegImm32 instruction);
    public void Accept(DiscriminatedNode discriminatedNode);
}