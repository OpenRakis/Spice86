namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>
/// A generated device operation invocation (<c>Cmos.WriteRegister(0x0B, value)</c>,
/// <c>Pit.ProgramChannel(0, 0x34, divisor)</c>, <c>Vga.SetPaletteEntry(index, r, g, b)</c>,
/// <c>Opl.WriteRegister(0x388, reg, value)</c>). <see cref="Ir.Passes.IrDeviceOperations"/>
/// replaces a recognized port sequence by it when nothing between the accesses can reach the
/// device; its effect is the same I/O token access the sequence's last access made.
/// </summary>
internal sealed record DeviceOperationCallNode : MethodCallNode {
    /// <summary>Creates the device operation invocation.</summary>
    /// <param name="propertyPath">The <c>CSharpOverrideHelper</c> device property the call is
    /// reached through (<c>Cmos</c>, <c>Pit</c>, <c>Vga</c>, <c>Opl</c>).</param>
    /// <param name="methodName">The device operation name.</param>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public DeviceOperationCallNode(string propertyPath, string methodName,
        params IVisitableAstNode[] arguments)
        : base(propertyPath, methodName, arguments) {
    }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new DeviceOperationCallNode(PropertyPath ?? string.Empty, MethodName, arguments);
}
