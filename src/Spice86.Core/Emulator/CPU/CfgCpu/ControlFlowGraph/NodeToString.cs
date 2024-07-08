namespace Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;

using System.Linq;

/// <summary>
/// For logging and debugging purposes
/// </summary>
public class NodeToString {
    public string ToString(ICfgNode node) {
        return $"{node.Address} / {node.Id} / {node.GetType().Name}";
    }

    public string SuccessorsToString(ICfgNode node) {
        return string.Join($"{Environment.NewLine}", SuccessorsToEnumerableString(node));
    }

    private IEnumerable<string> SuccessorsToEnumerableString(ICfgNode node) {
        if (node is CfgInstruction cfgInstruction) {
            return SuccessorsToEnumerableString(cfgInstruction);
        }
        if (node is DiscriminatedNode discriminatedNode) {
            return SuccessorsToEnumerableString(discriminatedNode);
        }
        throw new ArgumentException($"Invalid node type {node.GetType().Name}");
    }

    private IEnumerable<string> SuccessorsToEnumerableString(CfgInstruction cfgInstruction) {
        return cfgInstruction.SuccessorsPerAddress.Select(e => $"{ToString(e.Value)}");
    }
    
    private IEnumerable<string> SuccessorsToEnumerableString(DiscriminatedNode discriminatedNode) {
        return discriminatedNode.SuccessorsPerDiscriminator.Select(e => $"{e.Key} => {ToString(e.Value)}");
    }
}