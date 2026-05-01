namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

/// <summary>
/// Builder for creating control flow structures in the AST, such as conditional assignments and if/else nodes.
/// </summary>
public class ControlFlowAstBuilder {
    private readonly ConstantAstBuilder _constant;

    public ControlFlowAstBuilder(ConstantAstBuilder constant) {
        _constant = constant;
    }

    /// <summary>
    /// Creates an if/else node that conditionally assigns one of two values to a destination.
    /// This is a ternary-like operation: destination = condition ? trueValue : falseValue
    /// </summary>
    /// <param name="dataType">The data type of the assignment</param>
    /// <param name="destination">The destination node (left-hand side)</param>
    /// <param name="condition">The boolean condition to evaluate</param>
    /// <param name="trueValue">The value to assign if the condition is true</param>
    /// <param name="falseValue">The value to assign if the condition is false</param>
    /// <returns>An IfElseNode representing the conditional assignment</returns>
    public IfElseNode TernaryAssign(DataType dataType, ValueNode destination, ValueNode condition, ValueNode trueValue, ValueNode falseValue) {
        BinaryOperationNode assignTrue = Assign(dataType, destination, trueValue);
        BlockNode trueCase = new BlockNode(assignTrue);
        
        BinaryOperationNode assignFalse = Assign(dataType, destination, falseValue);
        BlockNode falseCase = new BlockNode(assignFalse);
        
        return new IfElseNode(condition, trueCase, falseCase);
    }

    /// <summary>
    /// Creates a while loop node that executes a block while a condition is true.
    /// </summary>
    /// <param name="condition">The boolean condition to evaluate before each iteration.</param>
    /// <param name="body">The block to execute while the condition is true.</param>
    /// <returns>A WhileNode representing the while loop.</returns>
    public WhileNode While(ValueNode condition, BlockNode body) {
        return new WhileNode(condition, body);
    }

    /// <summary>
    /// Creates a for-semantics loop from initializer, condition, iteration, and body.
    /// Equivalent to: initializer; while (condition) { body; iteration; }
    /// </summary>
    /// <param name="initializer">Statement executed once before the loop.</param>
    /// <param name="condition">Boolean condition evaluated before each iteration.</param>
    /// <param name="iteration">Statement executed after each loop body execution.</param>
    /// <param name="body">The block to execute while the condition is true.</param>
    /// <returns>A BlockNode containing the initializer and while loop.</returns>
    public BlockNode For(IVisitableAstNode initializer, ValueNode condition, IVisitableAstNode iteration, BlockNode body) {
        List<IVisitableAstNode> whileStatements = new(body.Statements.Count + 1);
        foreach (IVisitableAstNode statement in body.Statements) {
            whileStatements.Add(statement);
        }
        whileStatements.Add(iteration);

        BlockNode whileBody = new BlockNode(whileStatements.ToArray());
        WhileNode whileNode = While(condition, whileBody);
        return new BlockNode(initializer, whileNode);
    }

    /// <summary>
    /// Creates a control-flow if/else for conditional near jumps.
    /// True case executes a near jump to targetIp, false case advances IP to the next instruction.
    /// </summary>
    /// <param name="instruction">Current instruction for branch context and next IP calculation.</param>
    /// <param name="condition">Boolean jump condition.</param>
    /// <param name="targetIp">Near jump target IP.</param>
    /// <returns>An IfElseNode representing jump-or-fallthrough behavior.</returns>
    public IfElseNode ConditionalNearJump(CfgInstruction instruction, ValueNode condition, ValueNode targetIp) {
        JumpNearNode jumpNode = new JumpNearNode(instruction, targetIp);
        MoveIpNextNode fallthroughNode = new MoveIpNextNode(_constant.ToNode(instruction.NextInMemoryAddress.Offset));
        return new IfElseNode(condition, jumpNode, fallthroughNode);
    }

    /// <summary>
    /// Creates a control-flow if/else for a conditional interrupt call.
    /// True case executes the interrupt, false case advances IP to the next instruction.
    /// </summary>
    /// <param name="instruction">Current instruction for branch context and next IP calculation.</param>
    /// <param name="condition">Boolean condition that triggers the interrupt.</param>
    /// <param name="vectorNumber">The interrupt vector number to call when condition is true.</param>
    /// <returns>An IfElseNode representing interrupt-or-fallthrough behavior.</returns>
    public IfElseNode ConditionalInterrupt(CfgInstruction instruction, ValueNode condition, ValueNode vectorNumber) {
        InterruptCallNode interruptNode = new InterruptCallNode(instruction, vectorNumber);
        MoveIpNextNode fallthroughNode = new MoveIpNextNode(_constant.ToNode(instruction.NextInMemoryAddress.Offset));
        return new IfElseNode(condition, interruptNode, fallthroughNode);
    }

    /// <summary>
    /// Creates an if node with no else branch. The false case is an empty block.
    /// </summary>
    /// <param name="condition">The boolean condition to evaluate.</param>
    /// <param name="trueCase">The statement to execute when the condition is true.</param>
    /// <returns>An IfElseNode whose true branch is <paramref name="trueCase"/> and whose false branch is empty.</returns>
    public IfElseNode If(ValueNode condition, IVisitableAstNode trueCase) {
        return new IfElseNode(condition, trueCase, new BlockNode());
    }

    /// <summary>
    /// Creates a conditional throw: if <paramref name="condition"/> is true, throws a new <typeparamref name="TException"/> with <paramref name="message"/>.
    /// </summary>
    /// <typeparam name="TException">The exception type to throw. Must have a constructor accepting a single string message.</typeparam>
    /// <param name="condition">The boolean condition under which the exception is thrown.</param>
    /// <param name="message">The exception message.</param>
    /// <returns>An IfElseNode whose true branch throws the exception and whose false branch is empty.</returns>
    public IfElseNode ThrowIf<TException>(ValueNode condition, string message) where TException : Exception {
        ThrowNode throwNode = new ThrowNode(typeof(TException), message);
        return If(condition, new BlockNode(throwNode));
    }

    /// <summary>
    /// Creates an assignment node: destination = source
    /// </summary>
    /// <param name="dataType">The data type for the assignment</param>
    /// <param name="destination">The destination (left-hand side)</param>
    /// <param name="source">The source value (right-hand side)</param>
    /// <returns>BinaryOperationNode representing the assignment</returns>
    public BinaryOperationNode Assign(DataType dataType, ValueNode destination, ValueNode source) {
        return new BinaryOperationNode(dataType, destination, BinaryOperation.ASSIGN, source);
    }
}
