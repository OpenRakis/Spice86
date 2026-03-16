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
    private readonly AsmRenderingConfig _config;
    private readonly AstBuilder _astBuilder = new();
    private readonly AstInstructionRenderer _renderer;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeToString"/> class.
    /// </summary>
    /// <param name="config">The configuration for ASM rendering.</param>
    public NodeToString(AsmRenderingConfig config) {
        _config = config;
        _renderer = new AstInstructionRenderer(config);
    }

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

    public string ToAssemblyStringWithAddress(ICfgNode node) {
        string address = node.Address.ToString();
        string instruction = ToAssemblyString(node);
        return $"{AddSpaces(address)} {instruction}";
    }
    
    private string AddSpaces(string address) {
        string additionalSpaces = "";
        if (_config.AddressRightSpaces > 0) {
            additionalSpaces = new(' ', _config.AddressRightSpaces);
        }
        return address + additionalSpaces;
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
        return selectorNode.SuccessorsPerSignature.Select(e => $"{e.Key} => {ToString(e.Value)}");
    }
}