namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

[MovRmSignExtend(DestSize: 16, SourceSize: 8, SourceSignedType: "sbyte", DestUnsignedType: "ushort")]
public partial class MovRmSignExtendByte16;

[MovRmSignExtend(DestSize: 32, SourceSize: 8, SourceSignedType: "sbyte", DestUnsignedType: "uint")]
public partial class MovRmSignExtendByte32;

[MovRmSignExtend(DestSize: 32, SourceSize: 16, SourceSignedType: "short", DestUnsignedType: "uint")]
public partial class MovRmSignExtendWord32;