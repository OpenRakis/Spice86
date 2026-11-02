namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Builder for I/O port operation AST nodes (IN/OUT instructions).
/// Factorizes calls to helper.In8/In16/In32 and helper.Out8/Out16/Out32.
/// </summary>
public class IoAstBuilder {
    private readonly TypeConversionAstBuilder _typeConversion;

    public IoAstBuilder(TypeConversionAstBuilder typeConversion) {
        _typeConversion = typeConversion;
    }

    /// <summary>
    /// Creates a port read node (IN instruction): reads a value of the given data type from the port.
    /// The port node is cast to ushort if needed to match the In8/In16/In32 method signatures.
    /// </summary>
    public MethodCallValueNode IoRead(DataType dataType, ValueNode portNode) {
        ValueNode portAsUInt16 = _typeConversion.Convert(DataType.UINT16, portNode);
        return new PortReadCallNode(dataType.BitWidth, portAsUInt16);
    }

    /// <summary>
    /// Creates a port write node (OUT instruction): writes a value of the given data type to the port.
    /// The port node is cast to ushort if needed to match the Out8/Out16/Out32 method signatures.
    /// </summary>
    public MethodCallNode IoWrite(DataType dataType, ValueNode portNode, ValueNode valueNode) {
        ValueNode portAsUInt16 = _typeConversion.Convert(DataType.UINT16, portNode);
        return new PortWriteCallNode(dataType.BitWidth, portAsUInt16, valueNode);
    }
}
