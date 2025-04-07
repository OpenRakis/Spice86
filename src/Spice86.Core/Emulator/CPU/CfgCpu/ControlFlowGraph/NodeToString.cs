namespace Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;

using System.Linq;

/// <summary>
/// For logging and debugging purposes
/// </summary>
public class NodeToString {
    private readonly AstBuilder _astBuilder = new();
    private readonly AstInstructionRenderer _renderer = new();

    public string ToString(ICfgNode node) {
        return $"{ToHeaderString(node)} / {ToAssemblyString(node)}";
    }

    public string ToHeaderString(ICfgNode node) {
        return $"{node.Address} / {node.Id}";
    }

    public string ToAssemblyString(ICfgNode node) {
        InstructionNode ast = node.ToInstructionAst(_astBuilder);
        return ast.Accept(_renderer);
    }

    public string SuccessorsToString(ICfgNode node) {
        return string.Join($"{Environment.NewLine}", SuccessorsToEnumerableString(node));
    }

    private IEnumerable<string> SuccessorsToEnumerableString(ICfgNode node) {
        if (node is CfgInstruction cfgInstruction) {
            return SuccessorsToEnumerableString(cfgInstruction);
        }
        if (node is SelectorNode selectorNode) {
            return SuccessorsToEnumerableString(selectorNode);
        }
        throw new ArgumentException($"Invalid node type {node.GetType().Name}");
    }

    private IEnumerable<string> SuccessorsToEnumerableString(CfgInstruction cfgInstruction) {
        return cfgInstruction.SuccessorsPerAddress.Select(e => $"{ToString(e.Value)}");
    }
    
    private IEnumerable<string> SuccessorsToEnumerableString(SelectorNode selectorNode) {
        return selectorNode.SuccessorsPerDiscriminator.Select(e => $"{e.Key} => {ToString(e.Value)}");
    }
}