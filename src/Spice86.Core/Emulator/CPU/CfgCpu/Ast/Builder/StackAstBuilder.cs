namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.Registers;

/// <summary>
/// Helper class for creating stack operation nodes in the AST.
/// Wraps the atomic Stack.cs operations (Push16, Push32, Pop16, Pop32) to ensure
/// proper handling of SP modifications and memory access without edge cases.
/// This design reuses the tested Stack.cs logic which correctly handles cases like PUSH SP.
/// </summary>
public class StackAstBuilder {
    /// <summary>
    /// Creates an AST node for pushing a value onto the stack (16-bit).
    /// Generates a call to Stack.Push16(value) which atomically decrements SP and stores the value.
    /// </summary>
    /// <param name="valueNode">The 16-bit value to push</param>
    /// <returns>MethodCallNode representing Stack.Push16(value)</returns>
    public MethodCallNode Push16(ValueNode valueNode) {
        return new MethodCallNode("Stack", "Push16", valueNode);
    }

    /// <summary>
    /// Creates an AST node for pushing a value onto the stack (32-bit).
    /// Generates a call to Stack.Push32(value) which atomically decrements SP and stores the value.
    /// </summary>
    /// <param name="valueNode">The 32-bit value to push</param>
    /// <returns>MethodCallNode representing Stack.Push32(value)</returns>
    public MethodCallNode Push32(ValueNode valueNode) {
        return new MethodCallNode("Stack", "Push32", valueNode);
    }

    /// <summary>
    /// Creates an AST node for popping a value from the stack (16-bit).
    /// Generates a call to Stack.Pop16() which atomically loads the value and increments SP.
    /// </summary>
    /// <returns>MethodCallValueNode representing the result of Stack.Pop16()</returns>
    public MethodCallValueNode Pop16() {
        return new MethodCallValueNode(DataType.UINT16, "Stack", "Pop16");
    }

    /// <summary>
    /// Creates an AST node for popping a value from the stack (32-bit).
    /// Generates a call to Stack.Pop32() which atomically loads the value and increments SP.
    /// </summary>
    /// <returns>MethodCallValueNode representing the result of Stack.Pop32()</returns>
    public MethodCallValueNode Pop32() {
        return new MethodCallValueNode(DataType.UINT32, "Stack", "Pop32");
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
        if (dataType == DataType.UINT16 || dataType == DataType.INT16) {
            return Push16(valueNode);
        } else if (dataType == DataType.UINT32 || dataType == DataType.INT32) {
            return Push32(valueNode);
        }
        throw new ArgumentException($"Unsupported data type for push: {dataType}");
    }
}
