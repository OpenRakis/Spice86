namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>
/// The value-returning form of <see cref="DeviceOperationCallNode"/> (<c>Cmos.ReadRegister(0x0A)</c>):
/// a recognized port sequence whose last access is a read keeps its assignment target and replaces
/// the port read call by this node.
/// </summary>
internal sealed record DeviceOperationValueNode : MethodCallValueNode {
    /// <summary>Creates the value-returning device operation invocation.</summary>
    /// <param name="dataType">The data type of the returned value.</param>
    /// <param name="propertyPath">The <c>CSharpOverrideHelper</c> device property the call is
    /// reached through.</param>
    /// <param name="methodName">The device operation name.</param>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public DeviceOperationValueNode(DataType dataType, string propertyPath, string methodName,
        params IVisitableAstNode[] arguments)
        : base(dataType, new DeviceOperationCallNode(propertyPath, methodName, arguments)) {
    }

    /// <inheritdoc />
    public override MethodCallValueNode WithArguments(IVisitableAstNode[] arguments) =>
        new DeviceOperationValueNode(DataType, CallNode.PropertyPath ?? string.Empty,
            CallNode.MethodName, arguments);
}
