namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using System.Collections.Generic;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Helper class for creating stack operation nodes in the AST.
/// Wraps the atomic Stack.cs operations (Push16, Push32, Pop16, Pop32) to ensure
/// proper handling of SP modifications and memory access without edge cases.
/// This design reuses the tested Stack.cs logic which correctly handles cases like PUSH SP.
/// </summary>
public class StackAstBuilder {
    /// <summary>
    /// Creates an AST node for pushing a value onto the stack.
    /// Generates a call to Stack.Push16/Push32(value) which atomically decrements SP and stores the value.
    /// </summary>
    /// <param name="bitWidth">The bit width determining which push to use</param>
    /// <param name="valueNode">The value to push</param>
    /// <returns>MethodCallNode representing Stack.PushN(value)</returns>
    public MethodCallNode Push(BitWidth bitWidth, ValueNode valueNode) {
        return new MethodCallNode("Stack", $"Push{(int)bitWidth}", valueNode);
    }

    /// <summary>
    /// Creates an AST node for popping a value from the stack.
    /// Generates a call to Stack.Pop16/Pop32() which atomically loads the value and increments SP.
    /// </summary>
    /// <param name="bitWidth">The bit width determining which pop to use</param>
    /// <returns>MethodCallValueNode representing the pop result</returns>
    public MethodCallValueNode Pop(BitWidth bitWidth) {
        DataType dataType = DataType.UnsignedFromBitWidth(bitWidth);
        return new MethodCallValueNode(dataType, "Stack", $"Pop{(int)bitWidth}");
    }

    public MethodCallValueNode Peek(BitWidth bitWidth) {
        DataType dataType = DataType.UnsignedFromBitWidth(bitWidth);
        return new MethodCallValueNode(dataType, "Stack", $"Peek{(int)bitWidth}", new ConstantNode(DataType.UINT32, 0));
    }

    /// <summary>
    /// Creates an AST node for 32-bit LEAVE stack semantics.
    /// </summary>
    /// <returns>MethodCallNode representing 32-bit LEAVE.</returns>
    public MethodCallNode Leave32() {
        return new MethodCallNode("Stack", nameof(Stack.Leave32));
    }

    /// <summary>
    /// Creates an AST node for 16-bit LEAVE stack semantics.
    /// </summary>
    /// <returns>MethodCallNode representing 16-bit LEAVE.</returns>
    public MethodCallNode Leave16() {
        return new MethodCallNode("Stack", nameof(Stack.Leave16));
    }

    /// <summary>
    /// Dispatcher method for pushing a value based on its data type.
    /// For 16-bit types (UINT16, INT16), calls Push16.
    /// For 32-bit types (UINT32, INT32), calls Push32.
    /// </summary>
    /// <param name="dataType">The data type of the value to push</param>
    /// <param name="valueNode">The value to push</param>
    /// <returns>MethodCallNode for the appropriate Stack.PushN method</returns>
    public MethodCallNode Push(DataType dataType, ValueNode valueNode) {
        return Push(dataType.BitWidth, valueNode);
    }

    /// <summary>
    /// Pushes multiple values onto the stack in order.
    /// Appends the generated push nodes to the provided statements list.
    /// </summary>
    /// <param name="statements">List to append push statements to</param>
    /// <param name="dataType">Data type (16 or 32 bits) of values to push</param>
    /// <param name="values">Value nodes to push, in order</param>
    public void PushValues(List<IVisitableAstNode> statements, DataType dataType, params ValueNode[] values) {
        foreach (ValueNode val in values) {
            statements.Add(Push(dataType, val));
        }
    }

    /// <summary>
    /// Pops values from the stack and assigns them to the provided destinations in order.
    /// If a destination is null, the popped value is discarded.
    /// </summary>
    /// <param name="statements">List to append pop/assign statements to</param>
    /// <param name="dataType">Data type (16 or 32 bits) to pop</param>
    /// <param name="destinations">Destination value nodes (null means discard)</param>
    public void PopValues(List<IVisitableAstNode> statements, DataType dataType, params ValueNode?[] destinations) {
        foreach (ValueNode? dest in destinations) {
            MethodCallValueNode popCall = Pop(dataType.BitWidth);
            if (dest is null) {
                statements.Add(popCall);
            } else {
                statements.Add(new BinaryOperationNode(dataType, dest, BinaryOperation.ASSIGN, popCall));
            }
        }
    }

    /// <summary>
    /// Dispatcher method for LEAVE stack semantics based on bit width.
    /// For 16-bit, calls Leave16.
    /// For 32-bit, calls Leave32.
    /// </summary>
    /// <param name="bitWidth">The bit width determining which LEAVE to use</param>
    /// <returns>MethodCallNode representing the appropriate Stack.LeaveN method</returns>
    public MethodCallNode Leave(BitWidth bitWidth) {
        return bitWidth == BitWidth.DWORD_32 ? Leave32() : Leave16();
    }
}
