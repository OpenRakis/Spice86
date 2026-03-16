namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

/// <summary>
/// Represents a callback instruction execution in the AST.
/// Calls the callback handler with the given callback number, then conditionally advances IP
/// if the callback did not perform a jump.
/// </summary>
public class CallbackNode : CfgInstructionNode {
    /// <summary>
    /// Initializes a new instance of the <see cref="CallbackNode"/> class.
    /// </summary>
    /// <param name="instruction">The callback instruction (used for IP comparison and advancement).</param>
    /// <param name="callbackNumber">A node representing the callback number to dispatch.</param>
    public CallbackNode(CfgInstruction instruction, ValueNode callbackNumber) : base(instruction) {
        CallbackNumber = callbackNumber;
    }

    /// <summary>The callback number to dispatch.</summary>
    public ValueNode CallbackNumber { get; }

    /// <inheritdoc />
    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitCallbackNode(this);
    }
}
