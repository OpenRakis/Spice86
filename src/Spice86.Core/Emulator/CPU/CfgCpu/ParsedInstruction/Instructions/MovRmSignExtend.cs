namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

[MovRmZeroExtend(DestSize: 16, SourceSize: 8)]
public partial class MovRmZeroExtendByte16;

[MovRmZeroExtend(DestSize: 32, SourceSize: 8)]
public partial class MovRmZeroExtendByte32;

[MovRmZeroExtend(DestSize: 32, SourceSize: 16)]
public partial class MovRmZeroExtendWord32;